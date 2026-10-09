using System;
using System.Collections.Generic;

namespace ClubClash
{
    public enum BattleMode { Cpu, Local, Practice }
    public enum CpuDifficulty { Level1 = 1, Level2 = 2, Level3 = 3, Level4 = 4, Level5 = 5,
        Easy = Level1, Normal = Level3, Hard = Level4 }
    public enum CpuPersonality { Random, Rushdown, KeepDistance, Ranged }
    public enum BattlePhase { Countdown, Fight, RoundOver, MatchOver }

    [Serializable]
    public sealed class BattleOptions
    {
        public string P1 = "kendo", P2 = "soccer";
        public StageId Stage = StageId.Ground;
        // Optional per-match loadout snapshots. These can later replace a ball or
        // move without changing the shared catalog or the opposing player.
        public ClubDefinition P1Definition, P2Definition;
        public BattleMode Mode = BattleMode.Cpu;
        public CpuDifficulty Difficulty = CpuDifficulty.Normal;
        // Retained for serialized callers from older versions. Strategy is now
        // selected by the CPU at match start rather than supplied by the player.
        public CpuPersonality Personality = CpuPersonality.Random;
        public uint Seed = 74291;
    }

    public struct InputFrame
    {
        public bool Left, Right, Jump, Guard, A, B, Down;
        public static readonly InputFrame Empty = new InputFrame();
    }

    public struct HitBox
    {
        public float X, Y, Width, Height;
        public int Owner;
        public string Type;
        public bool Overlaps(HitBox other)
        {
            return X < other.X + other.Width && X + Width > other.X
                && Y < other.Y + other.Height && Y + Height > other.Y;
        }
    }

    public sealed class AttackState
    {
        public MoveDefinition Move;
        public string Button;
        public float Elapsed, Total;
        public float ChargeTravel, ChargeOriginX;
        public bool BikeEnded;
        public int Combo;
        public bool Air, Connected, Hit, Queued, Spawned;
    }

    public sealed class Fighter
    {
        public readonly ClubDefinition Club;
        public readonly int Index;
        public string ClubId { get { return Club.Id; } }
        public float X, Y, Vx, Vy, Facing;
        public float Hp, MaxHp = 120, Stamina, MaxStamina = 100;
        public float GuardBroken, Hitstun, Flash, Invulnerable, BCooldown;
        public bool HomeAdvantage;
        public float StatMultiplier;
        public int PoisonTicksRemaining;
        public float PoisonTickTimer;
        public bool IsPoisoned { get { return PoisonTicksRemaining > 0; } }
        public float PoisonRemaining { get { return IsPoisoned ? PoisonTicksRemaining - 1 + Math.Max(0, PoisonTickTimer) : 0; } }
        internal float PoisonDamage;
        internal int PoisonOwner;
        public bool Guard, Grounded;
        public string State, Animation;
        public AttackState Attack;
        public bool IsBicycling { get { return Attack != null && Attack.Move.Kind == "bike" && !Attack.BikeEnded
            && Attack.Elapsed >= Attack.Move.Startup && Attack.Elapsed < Attack.Move.Startup + Attack.Move.Active && Grounded; } }
        internal int ComboStage;
        internal float ComboWindow, LastHitAge;

        public Fighter(string clubId, int index) : this(Catalog.Get(clubId), index, StageId.Ground) { }
        public Fighter(ClubDefinition definition, int index) : this(definition, index, StageId.Ground) { }
        public Fighter(ClubDefinition definition, int index, StageId stage)
        {
            Club = (definition ?? Catalog.Clubs[0]).Copy(); Index = index;
            HomeAdvantage = Club.HomeStage == stage; StatMultiplier = HomeAdvantage ? 1.3f : 1;
            if (HomeAdvantage)
            {
                Club.Stats.Speed *= StatMultiplier; Club.Stats.Jump *= StatMultiplier;
                Club.Stats.GuardScale /= StatMultiplier; Club.Stats.GuardCost /= StatMultiplier;
                foreach (var move in Club.LightMoves) ScaleMove(move, StatMultiplier);
                ScaleMove(Club.StrongMove, StatMultiplier);
            }
            MaxHp = 120 * StatMultiplier; MaxStamina = 100 * StatMultiplier; Reset();
        }

        static void ScaleMove(MoveDefinition move, float multiplier)
        {
            move.Damage *= multiplier; move.GuardDamage *= multiplier; move.PoisonTickDamage *= multiplier;
            move.Startup /= multiplier; move.Active /= multiplier; move.Total /= multiplier; move.Cooldown /= multiplier;
            move.Lunge *= multiplier;
            if (move.Projectile != null)
            {
                // Preserve the spatial arc and finite range while making flight faster.
                move.Projectile.Speed *= multiplier; move.Projectile.Vy *= multiplier;
                move.Projectile.Gravity *= multiplier * multiplier; move.Projectile.Life /= multiplier;
            }
        }
        public void Reset()
        {
            X = Index == 0 ? 350 : 870; Y = Vx = Vy = 0; Facing = Index == 0 ? 1 : -1;
            Hp = MaxHp; Stamina = MaxStamina; Guard = false; Grounded = true;
            GuardBroken = Hitstun = Flash = Invulnerable = BCooldown = 0;
            Attack = null; State = Animation = "idle"; ComboStage = 0; ComboWindow = 0; LastHitAge = 999;
            ClearPoison();
        }

        internal void ClearPoison() { PoisonTicksRemaining = 0; PoisonTickTimer = PoisonDamage = 0; PoisonOwner = -1; }

        public HitBox Hurtbox()
        {
            return new HitBox { X = X - World.FighterWidth / 2, Y = Y + 5,
                Width = World.FighterWidth, Height = World.BodyHeight - 5, Owner = Index, Type = "hurt" };
        }
    }

    public sealed class Projectile
    {
        public long Id;
        public int Owner;
        public string Kind, Color;
        public float X, Y, Vx, Vy, Radius, Gravity, Life, Age;
        public float MaxRange, Distance;
        public float Damage, Knockback, GuardDamage, Stun;
        public int PoisonTicks;
        public float PoisonTickDamage;
    }

    public sealed class BattleEvent
    {
        public long Id;
        public string Type, Kind, Button;
        public int Owner = -1, Target = -1, Combo, Winner = -1;
        public float X, Y, Damage;
        public bool Air, Projectile;
    }

    // Pure C# simulation, independent of rendering, frame rate, and Unity scene lifetime.
    public sealed class Battle
    {
        const float StepSize = 1f / 120f;
        readonly BattleOptions options;
        readonly InputFrame[] previous = new InputFrame[2];
        readonly float[,] buffers = new float[2, 3];
        readonly List<PendingStrike> pendingStrikes = new List<PendingStrike>(2);
        uint rng;
        long eventId, projectileId;
        CpuBrain cpu;
        public Fighter[] Fighters { get; private set; }
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<BattleEvent> Events = new List<BattleEvent>();
        public float Time { get; private set; }
        public float Countdown { get; private set; }
        public float Hitstop { get; private set; }
        public BattlePhase Phase { get; private set; }
        public BattleMode Mode { get { return options.Mode; } }
        public StageId Stage { get { return options.Stage; } }
        public CpuDifficulty Difficulty { get { return options.Difficulty; } }
        public CpuPersonality Personality { get; private set; }
        public CpuPersonality EffectiveCpuPersonality { get { return Personality; } }
        public float CpuTargetDistance { get { return cpu == null ? 0 : cpu.TargetDistance; } }
        public float PracticeDamage { get; private set; }
        public int PracticeHits { get; private set; }
        public bool Paused { get; private set; }
        public int Winner { get; private set; }
        public int RoundWinner { get; private set; }
        public int Round { get; private set; }
        public int[] Wins { get; private set; }
        public InputFrame CpuInput { get; private set; }

        struct PendingStrike
        {
            public Fighter Target;
            public int Owner;
            public MoveDefinition Move;
            public float SourceX;
        }

        public Battle(BattleOptions settings = null)
        {
            settings = settings ?? new BattleOptions();
            options = new BattleOptions { P1 = settings.P1, P2 = settings.P2, Mode = settings.Mode,
                Difficulty = (CpuDifficulty)Math.Max(1, Math.Min(5, (int)settings.Difficulty)),
                Personality = CpuPersonality.Random, Seed = settings.Seed, Stage = settings.Stage,
                P1Definition = settings.P1Definition == null ? null : settings.P1Definition.Copy(),
                P2Definition = settings.P2Definition == null ? null : settings.P2Definition.Copy() };
            Restart();
        }

        public void Restart()
        {
            // Mix nearby match seeds before choosing a style so repeated matches
            // with small sequential seeds can produce all three personalities.
            rng = unchecked(((options.Seed == 0 ? 74291 : options.Seed) ^ 0x9e3779b9u) * 0x85ebca6bu);
            Fighters = new[] { new Fighter(options.P1Definition ?? Catalog.Get(options.P1), 0, options.Stage),
                new Fighter(Mode == BattleMode.Practice ? Catalog.Teacher : options.P2Definition ?? Catalog.Get(options.P2), 1, options.Stage) };
            Personality = (int)Difficulty <= 3
                ? (CpuPersonality)(1 + Math.Min(2, (int)(Random() * 3)))
                : Fighters[1].Club.StrongMove.Projectile == null ? CpuPersonality.Rushdown : CpuPersonality.Ranged;
            cpu = new CpuBrain((int)Difficulty, Personality, options.Seed);
            PracticeDamage = 0; PracticeHits = 0;
            Round = 1; Wins = new int[2]; Paused = false; ResetRound();
        }

        void ResetRound()
        {
            foreach (var f in Fighters) f.Reset();
            Projectiles.Clear(); Events.Clear(); Time = 60; Phase = BattlePhase.Countdown;
            Countdown = Mode == BattleMode.Practice ? 0 : 3;
            if (Mode == BattleMode.Practice) Phase = BattlePhase.Fight;
            Hitstop = 0; Winner = RoundWinner = -1; cpu.Reset(); CpuInput = InputFrame.Empty;
            ClearInputs();
        }

        void ClearInputs()
        {
            previous[0] = previous[1] = InputFrame.Empty;
            Array.Clear(buffers, 0, buffers.Length);
        }

        public bool NextRound()
        {
            if (Phase != BattlePhase.RoundOver) return false;
            Round++; ResetRound(); return true;
        }

        public void SetPaused(bool value) { Paused = value; ClearInputs(); }

        public bool SetPracticeClub(string clubId)
        {
            if (Mode != BattleMode.Practice) return false;
            ClubDefinition definition = null;
            foreach (var club in Catalog.Clubs) if (club.Id == clubId) { definition = club; break; }
            if (definition == null) return false;
            float x = Fighters[0].X;
            options.P1 = clubId; options.P1Definition = null;
            Fighters[0] = new Fighter(definition, 0, Stage) { X = Math.Min(808, x), Facing = 1 };
            Fighters[1].Reset(); Projectiles.Clear(); Events.Clear(); Hitstop = 0;
            ClearInputs(); CpuInput = InputFrame.Empty;
            Emit(new BattleEvent { Type = "practiceclub", Owner = 0, X = Fighters[0].X, Y = 100 });
            return true;
        }

        float Random()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return rng / 4294967296f;
        }

        void Emit(BattleEvent e) { e.Id = ++eventId; Events.Add(e); }

        public void Update(float dt, InputFrame player1, InputFrame player2)
        {
            Events.Clear();
            if (Paused || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            dt = Math.Min(dt, 5);
            if (Mode == BattleMode.Cpu) player2 = CpuControls(dt, player1);
            if (Mode == BattleMode.Practice) player2 = InputFrame.Empty;
            var input = new[] { player1, player2 };
            if (Phase == BattlePhase.Fight)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (input[i].A && !previous[i].A) buffers[i, 0] = .15f;
                    if (input[i].B && !previous[i].B) buffers[i, 1] = .15f;
                    if (input[i].Jump && !previous[i].Jump) buffers[i, 2] = .15f;
                }
            }
            previous[0] = input[0]; previous[1] = input[1];
            while (dt > .000001f)
            {
                float step = Math.Min(StepSize, dt);
                Tick(step, input); dt -= step;
            }
        }

        public void Update(float dt, InputFrame player1) { Update(dt, player1, InputFrame.Empty); }

        InputFrame CpuControls(float dt, InputFrame playerInput)
        {
            CpuInput = Phase == BattlePhase.Fight && Fighters[1].Hp > 0
                ? cpu.Update(dt, Fighters[1], Fighters[0], Projectiles, playerInput, previous[1]) : InputFrame.Empty;
            return CpuInput;
        }

        internal static bool ProjectileThreat(Projectile p, Fighter target, float horizon)
        {
            if (Math.Abs(p.Vx) < .001f) return false;
            float time = (target.X - p.X) / p.Vx;
            if (time < 0 || time > horizon || time > p.Life || p.Distance + Math.Abs(p.Vx) * time > p.MaxRange) return false;
            float height = p.Y + p.Vy * time - .5f * p.Gravity * time * time;
            return height + p.Radius >= target.Y + 5 && height - p.Radius <= target.Y + World.BodyHeight;
        }

        internal static bool CanStrongReach(Fighter f, Fighter opponent)
        {
            return CanStrongReachAtDistance(f, Math.Abs(opponent.X - f.X), opponent.Y);
        }

        // Public so editor validation can query finite attack reach across Unity's assembly boundary.
        public static bool CanStrongReachAtDistance(Fighter f, float distance, float opponentY)
        {
            var move = f.Club.StrongMove; var p = move.Projectile;
            if (move.ChargeDistance > 0) return f.Grounded && opponentY < move.High
                && distance <= move.ChargeDistance + move.Reach + 48;
            if (p == null) return distance <= move.Reach + 48;
            float travel = Math.Max(0, distance - 46 - World.FighterWidth / 2 - p.Radius);
            float time = travel / p.Speed;
            if (travel > p.Range || time > p.Life) return false;
            float height = f.Y + p.Height + p.Vy * time - .5f * p.Gravity * time * time;
            return height + p.Radius >= opponentY + 5 && height - p.Radius <= opponentY + World.BodyHeight;
        }

        void Tick(float dt, InputFrame[] inputs)
        {
            if (Phase == BattlePhase.Countdown)
            {
                Countdown = Math.Max(0, Countdown - dt);
                if (Countdown < .00005f)
                { Countdown = 0; Phase = BattlePhase.Fight; Emit(new BattleEvent { Type = "fight" }); }
                return;
            }
            if (Phase == BattlePhase.RoundOver || Phase == BattlePhase.MatchOver)
            {
                foreach (var f in Fighters) { Settle(f, dt); SetAnimation(f); }
                return;
            }
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 3; j++) buffers[i, j] = Math.Max(0, buffers[i, j] - dt);
            // Strong attack cooldown is separate from recovery: during the remaining
            // cooldown players can move, defend, jump, and use their light attack.
            foreach (var f in Fighters) f.BCooldown = Math.Max(0, f.BCooldown - dt);
            foreach (var f in Fighters) TickPoison(f, dt);
            if (Mode != BattleMode.Practice && (Fighters[0].Hp <= 0 || Fighters[1].Hp <= 0)) { EndRound(); return; }
            if (Hitstop > 0) { Hitstop = Math.Max(0, Hitstop - dt); return; }
            if (Mode != BattleMode.Practice) Time = Math.Max(0, Time - dt);
            FighterStep(Fighters[0], Fighters[1], dt, inputs[0]);
            if (Mode == BattleMode.Practice) TeacherStep(Fighters[1], dt);
            else FighterStep(Fighters[1], Fighters[0], dt, inputs[1]);
            PushFighters();
            pendingStrikes.Clear();
            foreach (var f in Fighters)
            {
                var attack = f.Attack;
                if (attack == null || attack.Elapsed < attack.Move.Startup
                    || attack.Elapsed >= attack.Move.Startup + attack.Move.Active) continue;
                if (attack.Move.Projectile != null && !attack.Spawned)
                { SpawnProjectile(f, attack); attack.Spawned = true; }
                if (attack.Move.Projectile == null && !attack.Hit && !attack.BikeEnded)
                {
                    var target = Fighters[1 - f.Index];
                    if (target.Hp > 0 && target.Invulnerable <= 0 && AttackBox(f).Overlaps(target.Hurtbox()))
                    {
                        attack.Hit = attack.Connected = true;
                        if (attack.Move.Kind == "bike") EndBike(f, attack);
                        pendingStrikes.Add(new PendingStrike { Target = target, Owner = f.Index, Move = attack.Move, SourceX = f.X });
                    }
                }
            }
            // Detect both strikes before resolving damage so equal-frame attacks trade
            // fairly instead of granting an implicit advantage to player one.
            foreach (var strike in pendingStrikes) Damage(strike.Target, strike.Owner, strike.Move, strike.SourceX, false);
            ProjectileStep(dt);
            if (Mode != BattleMode.Practice && (Fighters[0].Hp <= 0 || Fighters[1].Hp <= 0 || Time <= 0)) EndRound();
            foreach (var f in Fighters) SetAnimation(f);
        }

        void TickPoison(Fighter f, float dt)
        {
            if (!f.IsPoisoned || f.Hp <= 0) return;
            f.PoisonTickTimer -= dt;
            while (f.PoisonTickTimer <= .00005f && f.PoisonTicksRemaining > 0)
            {
                f.PoisonTicksRemaining--; f.PoisonTickTimer += 1;
                if (Mode == BattleMode.Practice && f.Index == 1) PracticeDamage += f.PoisonDamage;
                else f.Hp = Math.Max(0, f.Hp - f.PoisonDamage);
                f.LastHitAge = 0; f.Flash = .12f;
                Emit(new BattleEvent { Type = "poisontick", Kind = "flask", Owner = f.PoisonOwner,
                    Target = f.Index, X = f.X, Y = f.Y + 100, Damage = f.PoisonDamage });
            }
            if (!f.IsPoisoned) f.PoisonTickTimer = 0;
        }

        static void TeacherStep(Fighter f, float dt)
        {
            SettleTimers(f, dt);
            f.X = 870; f.Y = f.Vx = f.Vy = 0; f.Grounded = true; f.Facing = -1;
            f.Guard = false; f.Attack = null; f.Hp = f.MaxHp; f.Stamina = f.MaxStamina;
        }

        void Settle(Fighter f, float dt)
        {
            f.Flash = Math.Max(0, f.Flash - dt); f.Hitstun = Math.Max(0, f.Hitstun - dt);
            f.GuardBroken = Math.Max(0, f.GuardBroken - dt);
            if (!f.Grounded || f.Y > 0)
            {
                f.Vy -= World.Gravity * dt; f.Y += f.Vy * dt;
                if (f.Y <= 0) { f.Y = f.Vy = 0; f.Grounded = true; }
            }
        }

        void FighterStep(Fighter f, Fighter opponent, float dt, InputFrame input)
        {
            SettleTimers(f, dt);
            if (f.Attack == null && !input.Guard && f.Hitstun <= 0 && Math.Abs(opponent.X - f.X) > 2)
                f.Facing = opponent.X > f.X ? 1 : -1;
            // Holding guard preserves the committed direction; crossing behind beats it.
            f.Guard = input.Guard && f.Grounded && f.Hitstun <= 0 && f.GuardBroken <= 0 && f.Attack == null && f.Hp > 0;
            f.Stamina = Clamp(f.Stamina + dt * (f.Guard ? 4 : f.GuardBroken > 0 ? 6 : 25) * f.StatMultiplier, 0, f.MaxStamina);
            if (f.Hp > 0 && f.Hitstun <= 0 && f.GuardBroken <= 0)
            {
                if (buffers[f.Index, 2] > 0 && f.Grounded && f.Attack == null)
                {
                    buffers[f.Index, 2] = 0; f.Vy = f.Club.Stats.Jump; f.Grounded = false; f.Guard = false;
                    Emit(new BattleEvent { Type = "jump", Owner = f.Index, X = f.X, Y = f.Y });
                }
                if (buffers[f.Index, 0] > 0)
                {
                    if (f.Attack != null && f.Attack.Button == "a" && !f.Attack.Air && f.Attack.Combo < 2 && f.Attack.Elapsed >= .055f)
                    { f.Attack.Queued = true; buffers[f.Index, 0] = 0; }
                    else if (f.Attack == null)
                    { StartAttack(f, "a", f.ComboWindow > 0 ? Math.Min(2, f.ComboStage) : 0); buffers[f.Index, 0] = 0; }
                }
                else if (buffers[f.Index, 1] > 0 && f.Attack == null && f.BCooldown <= 0)
                { StartAttack(f, "b", 0); buffers[f.Index, 1] = 0; }
            }
            bool locked = f.Attack != null || f.Hitstun > 0 || f.GuardBroken > 0 || f.Hp <= 0;
            if (!locked)
            {
                int direction = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
                float speed = f.Club.Stats.Speed * (f.Guard ? f.Club.Stats.GuardMove : input.Down ? .5f : 1);
                f.Vx = Approach(f.Vx, direction * speed, dt * (f.Grounded ? 3000 : 2000));
            }
            else if (f.Attack != null && f.Attack.Move.Kind == "bike")
                f.Vx = f.IsBicycling ? f.Facing * f.Attack.Move.Lunge : 0;
            else if (f.Attack != null && f.Attack.Move.Lunge > 0
                && f.Attack.Elapsed >= f.Attack.Move.Startup - .06f
                && f.Attack.Elapsed < f.Attack.Move.Startup + f.Attack.Move.Active)
                f.Vx = f.Facing * f.Attack.Move.Lunge;
            else f.Vx = Approach(f.Vx, 0, dt * (f.Grounded ? 1800 : 500));
            float previousX = f.X;
            float travel = f.Vx * dt;
            if (f.IsBicycling)
                travel = f.Facing * Math.Min(Math.Abs(travel), Math.Max(0, f.Attack.Move.ChargeDistance - f.Attack.ChargeTravel));
            f.X = Clamp(f.X + travel, 36, World.Width - 36);
            if (f.IsBicycling)
            {
                var bike = f.Attack;
                bike.ChargeTravel += Math.Abs(f.X - previousX);
                if (bike.ChargeTravel >= bike.Move.ChargeDistance - .001f || Math.Abs(f.X - previousX - travel) > .001f)
                    EndBike(f, bike);
            }
            if (!f.Grounded)
            {
                f.Vy -= World.Gravity * dt; f.Y += f.Vy * dt;
                if (f.Y <= 0) { f.Y = f.Vy = 0; f.Grounded = true; }
            }
            if (f.Attack != null)
            {
                var attack = f.Attack; attack.Elapsed += dt;
                if (attack.Move.Kind == "bike" && !attack.BikeEnded
                    && attack.Elapsed >= attack.Move.Startup + attack.Move.Active) EndBike(f, attack);
                if (attack.Elapsed >= attack.Total)
                {
                    f.Attack = null;
                    if (attack.Button == "a" && !attack.Air)
                    {
                        f.ComboStage = attack.Combo < 2 ? attack.Combo + 1 : 0;
                        f.ComboWindow = attack.Combo < 2 ? .28f : 0;
                        if (attack.Queued && attack.Combo < 2 && f.Hp > 0 && f.Hitstun <= 0)
                            StartAttack(f, "a", attack.Combo + 1);
                    }
                    else { f.ComboStage = 0; f.ComboWindow = 0; }
                }
            }
        }

        static void SettleTimers(Fighter f, float dt)
        {
            f.Flash = Math.Max(0, f.Flash - dt); f.Hitstun = Math.Max(0, f.Hitstun - dt);
            f.GuardBroken = Math.Max(0, f.GuardBroken - dt); f.Invulnerable = Math.Max(0, f.Invulnerable - dt);
            f.LastHitAge += dt; f.ComboWindow = Math.Max(0, f.ComboWindow - dt);
        }

        void EndBike(Fighter f, AttackState attack)
        {
            if (attack.BikeEnded) return;
            attack.BikeEnded = true; f.Vx = 0;
            Emit(new BattleEvent { Type = "bikeend", Kind = "bike", Owner = f.Index, X = f.X, Y = f.Y + 40 });
        }

        void StartAttack(Fighter f, string button, int combo)
        {
            bool air = !f.Grounded;
            var move = button == "a" ? f.Club.LightMoves[combo] : f.Club.StrongMove;
            if (air)
            {
                move = move.Copy(); move.Low = 10; move.High = 130;
                // Air strong remains usable as a kick; bicycles stay on the floor.
                if (move.Kind == "bike") { move.Kind = "kick"; move.ChargeDistance = move.Lunge = 0; move.Active = .12f; }
                move.Startup *= .8f; move.Total *= .86f; move.Damage = (float)Math.Round(move.Damage * .92f);
                if (move.Projectile != null) { move.Projectile.Vy = -160; if (move.Projectile.Gravity == 0) move.Projectile.Gravity = 160; }
            }
            f.Guard = false;
            f.Attack = new AttackState { Move = move, Button = button, Elapsed = 0, Total = move.Total, Combo = combo,
                Air = air, ChargeOriginX = f.X };
            if (button == "b") f.BCooldown = move.Cooldown;
            if (f.Grounded) f.Vx *= .25f;
            if (move.Kind == "bike") f.Vx = 0;
            Emit(new BattleEvent { Type = "attack", Owner = f.Index, Button = button, Combo = combo,
                Air = air, Kind = move.Kind, X = f.X, Y = f.Y + 100 });
        }

        HitBox AttackBox(Fighter f)
        {
            var move = f.Attack.Move; const float near = 15;
            return new HitBox { X = f.Facing == 1 ? f.X + near : f.X - near - move.Reach,
                Y = f.Y + move.Low, Width = move.Reach, Height = move.High - move.Low, Owner = f.Index, Type = "attack" };
        }

        void PushFighters()
        {
            var a = Fighters[0]; var b = Fighters[1];
            if (Mode == BattleMode.Practice)
            {
                if (Math.Abs(a.Y) <= 125 && Math.Abs(a.X - b.X) < 62)
                    a.X = a.X <= b.X ? b.X - 62 : b.X + 62;
                return;
            }
            if (Math.Abs(a.Y - b.Y) > 125) return;
            float delta = b.X - a.X; float distance = Math.Abs(delta); const float required = 62;
            if (distance >= required) return;
            float sign = delta >= 0 ? 1 : -1, amount = (required - distance) / 2;
            a.X = Clamp(a.X - sign * amount, 36, World.Width - 36); b.X = Clamp(b.X + sign * amount, 36, World.Width - 36);
            float remaining = required - Math.Abs(b.X - a.X);
            if (remaining > 0)
            {
                if (a.X <= 36 || a.X >= World.Width - 36) b.X = Clamp(b.X + sign * remaining, 36, World.Width - 36);
                else a.X = Clamp(a.X - sign * remaining, 36, World.Width - 36);
            }
        }

        void SpawnProjectile(Fighter f, AttackState attack)
        {
            var definition = attack.Move.Projectile;
            var projectile = new Projectile { Id = ++projectileId, Owner = f.Index,
                X = f.X + f.Facing * 46, Y = f.Y + definition.Height,
                Vx = definition.Speed * f.Facing, Vy = definition.Vy, Gravity = definition.Gravity,
                Radius = definition.Radius, Kind = definition.Kind, Color = f.Club.Color, Life = definition.Life,
                MaxRange = definition.Range, PoisonTicks = attack.Move.PoisonTicks, PoisonTickDamage = attack.Move.PoisonTickDamage,
                Damage = attack.Move.Damage, Knockback = attack.Move.Knockback,
                GuardDamage = attack.Move.GuardDamage, Stun = attack.Move.Stun };
            Projectiles.Add(projectile);
            Emit(new BattleEvent { Type = "projectile", Owner = f.Index, X = projectile.X, Y = projectile.Y, Kind = projectile.Kind });
        }

        void ProjectileStep(float dt)
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var p = Projectiles[i];
                float oldX = p.X, oldY = p.Y;
                float step = Math.Min(dt, Math.Max(0, p.Life));
                if (Math.Abs(p.Vx) > .001f) step = Math.Min(step, Math.Max(0, p.MaxRange - p.Distance) / Math.Abs(p.Vx));
                p.Life -= dt; p.Age += step; p.Vy -= p.Gravity * step;
                p.X += p.Vx * step; p.Y += p.Vy * step; p.Distance += Math.Abs(p.X - oldX);
                var target = Fighters[1 - p.Owner];
                if (target.Hp > 0 && target.Invulnerable <= 0 && SweptHit(oldX, oldY, p.X, p.Y, p.Radius, target.Hurtbox()))
                {
                    Damage(target, p.Owner, new MoveDefinition { Damage = p.Damage, Knockback = p.Knockback,
                        GuardDamage = p.GuardDamage, Stun = p.Stun, Color = p.Color, Kind = p.Kind,
                        PoisonTicks = p.PoisonTicks, PoisonTickDamage = p.PoisonTickDamage }, oldX, true, p.Y);
                    Projectiles.RemoveAt(i);
                }
                else if (p.Life <= 0 || p.Distance >= p.MaxRange - .001f || p.X < -50 || p.X > World.Width + 50 || p.Y < -p.Radius)
                    Projectiles.RemoveAt(i);
            }
        }

        static bool SweptHit(float x0, float y0, float x1, float y1, float radius, HitBox box)
        {
            float enter = 0, exit = 1;
            return SegmentAxis(x0, x1 - x0, box.X - radius, box.X + box.Width + radius, ref enter, ref exit)
                && SegmentAxis(y0, y1 - y0, box.Y - radius, box.Y + box.Height + radius, ref enter, ref exit);
        }

        static bool SegmentAxis(float origin, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (Math.Abs(delta) < .000001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            if (a > b) { float swap = a; a = b; b = swap; }
            enter = Math.Max(enter, a); exit = Math.Min(exit, b);
            return enter <= exit;
        }

        void Damage(Fighter target, int owner, MoveDefinition move, float sourceX, bool projectile, float impactY = -1)
        {
            bool inFront = (sourceX - target.X) * target.Facing >= 0;
            bool blocked = target.Guard && inFront && target.GuardBroken <= 0;
            bool broken = false; float damage = move.Damage;
            float hitY = impactY >= 0 ? impactY : target.Y + 100;
            if (move.PoisonTicks > 0)
            {
                target.PoisonTicksRemaining = move.PoisonTicks; target.PoisonTickTimer = 1;
                target.PoisonDamage = move.PoisonTickDamage; target.PoisonOwner = owner;
                Emit(new BattleEvent { Type = "poison", Kind = move.Kind, Owner = owner, Target = target.Index,
                    X = target.X, Y = target.Y + 100 });
            }
            if (blocked)
            {
                target.Stamina = Math.Max(0, target.Stamina - move.GuardDamage * target.Club.Stats.GuardCost);
                if (target.Stamina <= 0)
                {
                    broken = true; target.Guard = false; target.GuardBroken = .8f; target.Hitstun = .55f;
                    Emit(new BattleEvent { Type = "guardbreak", Owner = owner, Target = target.Index, X = target.X, Y = target.Y + 100 });
                }
                else
                {
                    damage = move.PoisonTicks > 0 ? 0 : Math.Max(1, (float)Math.Round(damage * target.Club.Stats.GuardScale)); target.Hitstun = .075f;
                    Emit(new BattleEvent { Type = "block", Owner = owner, Target = target.Index, X = target.X, Y = hitY,
                        Damage = damage, Projectile = projectile, Kind = move.Kind });
                }
            }
            bool practiceTeacher = Mode == BattleMode.Practice && target.Index == 1;
            if (practiceTeacher) { PracticeDamage += damage; PracticeHits++; }
            else target.Hp = Math.Max(0, target.Hp - damage);
            if (Mode == BattleMode.Cpu && owner == 1) cpu.OnConnected();
            target.LastHitAge = 0; target.Flash = blocked && !broken ? .08f : .16f;
            if (!blocked || broken)
            {
                target.Attack = null; target.ComboWindow = 0; target.ComboStage = 0;
                target.Hitstun = Math.Max(target.Hitstun, move.Stun);
                Emit(new BattleEvent { Type = "hit", Owner = owner, Target = target.Index, X = target.X, Y = hitY,
                    Damage = damage, Projectile = projectile, Kind = move.Kind });
            }
            float direction = sourceX <= target.X ? 1 : -1;
            target.Vx = practiceTeacher ? 0 : direction * move.Knockback * (blocked && !broken ? .25f : 1);
            if (!practiceTeacher && !blocked && move.Damage >= 26 && target.Grounded) { target.Vy = 110; target.Grounded = false; }
            Hitstop = Math.Max(Hitstop, blocked && !broken ? .028f : .042f);
        }

        void EndRound()
        {
            float a = Fighters[0].Hp, b = Fighters[1].Hp;
            int result = a == b ? -1 : a > b ? 0 : 1;
            Winner = RoundWinner = result;
            if (result >= 0) Wins[result]++;
            Phase = result >= 0 && Wins[result] >= 2 ? BattlePhase.MatchOver : BattlePhase.RoundOver;
            foreach (var f in Fighters) { f.Guard = false; f.Attack = null; f.Vx = 0; SetAnimation(f); }
            Projectiles.Clear(); Emit(new BattleEvent { Type = "roundend", Winner = result });
            if (Phase == BattlePhase.MatchOver) Emit(new BattleEvent { Type = "matchend", Winner = result });
        }

        static void SetAnimation(Fighter f)
        {
            f.State = f.Hp <= 0 ? "down" : f.Hitstun > 0 || f.GuardBroken > 0 ? "hurt" : f.Attack != null ? "attack"
                : f.Guard ? "guard" : !f.Grounded ? "jump" : Math.Abs(f.Vx) > 20 ? "run" : "idle";
            f.Animation = f.State != "attack" ? f.State : f.Attack.Air ? (f.Attack.Button == "a" ? "airA" : "airB")
                : f.Attack.Button == "a" ? "attackA" + (f.Attack.Combo + 1) : "attackB";
        }

        public List<HitBox> GetHitboxes()
        {
            var result = new List<HitBox> { Fighters[0].Hurtbox(), Fighters[1].Hurtbox() };
            foreach (var f in Fighters)
                if (f.Attack != null && f.Attack.Move.Projectile == null && f.Attack.Elapsed >= f.Attack.Move.Startup
                    && f.Attack.Elapsed < f.Attack.Move.Startup + f.Attack.Move.Active && !f.Attack.BikeEnded) result.Add(AttackBox(f));
            foreach (var p in Projectiles) result.Add(new HitBox { X = p.X - p.Radius, Y = p.Y - p.Radius,
                Width = p.Radius * 2, Height = p.Radius * 2, Type = "projectile", Owner = p.Owner });
            return result;
        }

        static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
        static float Approach(float value, float target, float amount)
        { return value < target ? Math.Min(target, value + amount) : Math.Max(target, value - amount); }
    }
}
