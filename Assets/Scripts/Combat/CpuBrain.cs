using System;
using System.Collections.Generic;

namespace ClubClash
{
    // The CPU only returns the same four actions and horizontal movement as a player.
    // Higher levels improve perception, spacing and commitment; they never change stats.
    internal sealed class CpuBrain
    {
        readonly int level;
        readonly CpuPersonality personality;
        readonly uint seed;
        uint rng;
        float clock, decisionTimer, attackGap, movementUntil, projectileMemory, guardMemory;
        int moveDirection;
        readonly float[] decisionPeriods = { 0, .52f, .32f, .19f, .085f, .045f };
        public float TargetDistance { get; private set; }

        public CpuBrain(int difficulty, CpuPersonality style, uint matchSeed)
        { level = difficulty; personality = style; seed = matchSeed == 0 ? 74291u : matchSeed; Reset(); }

        public void Reset()
        {
            rng = seed ^ 0x9e3779b9u; clock = decisionTimer = attackGap = movementUntil = projectileMemory = guardMemory = 0;
            moveDirection = 0;
            TargetDistance = 0;
        }

        float Random()
        { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return rng / 4294967296f; }

        public void OnConnected()
        {
            // Beginners visibly give the player room after a hit, including in corners.
            if (level == 1) attackGap = Math.Max(attackGap, .90f);
            else if (level == 2) attackGap = Math.Max(attackGap, .35f);
        }

        public InputFrame Update(float dt, Fighter f, Fighter op, List<Projectile> projectiles,
            InputFrame player, InputFrame previous)
        {
            clock += dt; decisionTimer -= dt; attackGap = Math.Max(0, attackGap - dt);
            projectileMemory = Math.Max(0, projectileMemory - dt); guardMemory = Math.Max(0, guardMemory - dt);
            var input = InputFrame.Empty;
            float distance = Math.Abs(op.X - f.X);
            int toward = op.X > f.X ? 1 : -1;
            bool rangedSpecialist = level >= 4 && f.Club.StrongMove.Projectile != null
                && f.Club.StrongMove.Projectile.Kind == "arrow";
            bool projectileStartup = op.Attack != null && op.Attack.Move.Projectile != null
                && op.Attack.Elapsed < op.Attack.Move.Startup + .07f;
            bool validFireInput = player.B && op.BCooldown <= 0 && op.Attack == null;
            if (level >= 4 && (projectileStartup || validFireInput)) projectileMemory = .85f;
            bool meleeThreat = op.Attack != null && op.Attack.Move.Projectile == null && !op.Attack.Hit
                && op.Attack.Elapsed < op.Attack.Move.Startup + op.Attack.Move.Active
                && (f.X - op.X) * op.Facing >= 0
                && distance < op.Attack.Move.Reach + 48;

            bool incoming = false, incomingPoison = false, lowIncoming = false, unsafeBowCommit = false;
            float impactTime = float.MaxValue, jumpClearance = 0;
            foreach (var p in projectiles)
            {
                if (p.Owner != 0 || Math.Abs(p.Vx) < .001f) continue;
                if (rangedSpecialist && Battle.ProjectileThreat(p, f, f.Club.StrongMove.Total + .10f))
                    unsafeBowCommit = true;
                float relativeSpeed = p.Vx - f.Vx;
                if (Math.Abs(relativeSpeed) < .001f) continue;
                float time = (f.X - p.X) / relativeSpeed;
                float horizon = level >= 4 ? .48f : level == 3 ? .30f : .16f;
                if (time < 0 || time > horizon || time > p.Life
                    || p.Distance + Math.Abs(p.Vx) * time > p.MaxRange) continue;
                float height = p.Y + p.Vy * time - .5f * p.Gravity * time * time;
                if (height + p.Radius < f.Y + 5 || height - p.Radius > f.Y + World.BodyHeight) continue;
                incoming = true; impactTime = Math.Min(impactTime, time);
                incomingPoison |= p.PoisonTicks > 0; lowIncoming |= height <= 140;
                jumpClearance = Math.Max(jumpClearance, height + p.Radius - 5);
                if (level >= 4) projectileMemory = .85f;
            }
            bool available = f.Attack == null && f.Hitstun <= 0 && f.GuardBroken <= 0;
            bool decide = decisionTimer <= 0;
            if (decide)
            {
                decisionTimer = decisionPeriods[level] * (.90f + Random() * .20f);
                float desired = DesiredDistance(f, op);
                // When a projectile user commits to its animation, close the gap rather
                // than letting repeated shots reset the same distant situation.
                if (level >= 4 && projectileMemory > 0 && !rangedSpecialist) desired = f.Club.LightMoves[0].Reach + 12;
                TargetDistance = desired;
                moveDirection = distance > desired + 22 ? toward : distance < desired - 26 ? -toward : 0;
                // A bow should not spend its cooldown running into punch range.
                // At the arena edge it must still use normal melee/guard/jump rules.
                if (rangedSpecialist && moveDirection == -toward && !RetreatRoom(f, toward)) moveDirection = 0;
                if (level >= 4 && available && distance <= f.Club.LightMoves[0].Reach + 44) moveDirection = 0;
                movementUntil = clock + (level == 1 ? .22f : level == 2 ? .24f : level == 3 ? .18f : .11f);
                float defendChance = level == 1 ? .18f : level == 2 ? .40f : level == 3 ? .66f : level == 4 ? .96f : 1;
                if (available && (meleeThreat || incoming) && Random() < defendChance)
                    guardMemory = level >= 4 ? .10f : .17f;
                if (available && attackGap <= 0 && !meleeThreat && !incoming)
                {
                    // Account for the opponent retreating during our own startup.
                    float retreat = Math.Max(0, op.Vx * (op.X > f.X ? 1 : -1));
                    float reachMargin = level >= 4 ? Math.Max(0, retreat * f.Club.LightMoves[0].Startup - 10) : 0;
                    bool lightReach = distance + reachMargin <= f.Club.LightMoves[0].Reach + 43
                        && Math.Abs(op.Y - f.Y) < 105;
                    bool punish = op.Attack != null && op.Attack.Elapsed > op.Attack.Move.Startup + op.Attack.Move.Active;
                    if (lightReach && Random() < (level == 1 ? .58f : level == 2 ? .78f : .97f))
                    {
                        // Slow teachers of the controls leave gaps; skilled CPUs confirm
                        // combos and occasionally punish recovery with a melee strong.
                        bool strongMelee = level >= 4 && f.Club.StrongMove.Projectile == null
                            && f.BCooldown <= 0 && punish && distance > f.Club.LightMoves[0].Reach - 8;
                        input.B = strongMelee; input.A = !strongMelee;
                        attackGap = level == 1 ? .75f : level == 2 ? .33f : level == 3 ? .13f : 0;
                    }
                    else if (f.BCooldown <= 0 && (f.Club.StrongMove.Projectile == null || f.Grounded)
                        && !(rangedSpecialist && (unsafeBowCommit || projectileStartup || validFireInput))
                        && Battle.CanStrongReach(f, op) && StrongWorthUsing(f, op, distance)
                        && Random() < (level == 1 ? .26f : level == 2 ? .48f : level == 3 ? .74f : .95f))
                    {
                        input.B = true; attackGap = level == 1 ? 1.05f : level == 2 ? .50f : .10f;
                        moveDirection = 0;
                    }
                }
                else if (f.Attack != null && f.Attack.Button == "a" && f.Attack.Combo < 2
                    && level >= 3 && f.Attack.Connected && distance <= f.Club.LightMoves[f.Attack.Combo + 1].Reach + 50
                    && Math.Abs(op.Y - f.Y) < 105 && Random() < (level == 3 ? .50f : level == 4 ? .88f : 1))
                    input.A = true;
            }
            if (level >= 4 && available)
            {
                // Reading a committed move/input is allowed at the two hardest levels.
                // A dodge still needs the normal grounded jump and its travel time.
                // A real jump must clear the projectile before contact. Bow users'
                // lower jump cannot clear every head-height ball; guard those shots.
                float discriminant = f.Club.Stats.Jump * f.Club.Stats.Jump - 2 * World.Gravity * jumpClearance;
                float riseTime = discriminant > 0 ? (f.Club.Stats.Jump - (float)Math.Sqrt(discriminant)) / World.Gravity : 999;
                float jumpLead = rangedSpecialist ? Math.Min(.43f, Math.Max(.21f, riseTime + (level == 5 ? .09f : .06f)))
                    : level == 5 ? .29f : .25f;
                // The bow's solid item guard gives a faster counter-shot window
                // than jumping every ordinary ball. Poison still calls for a dodge.
                bool wantsJump = rangedSpecialist ? incomingPoison && riseTime < .40f : incomingPoison || lowIncoming;
                if (incoming && wantsJump && impactTime < jumpLead
                    && f.Grounded && !previous.Jump)
                { input.Jump = true; guardMemory = 0;
                    moveDirection = rangedSpecialist ? (distance < TargetDistance - 24 && RetreatRoom(f, toward) ? -toward : 0) : toward; }
                else if (meleeThreat || (incoming && !input.Jump)) guardMemory = .075f;
            }
            if (available && guardMemory > 0 && !input.A && !input.B && !input.Jump && f.Stamina > 8)
                input.Guard = true;
            if (!input.Guard && !input.A && !input.B && clock <= movementUntil)
            { input.Left = moveDirection < 0; input.Right = moveDirection > 0; }
            if (level >= 4 && projectileMemory > 0 && !rangedSpecialist && !input.Guard && !input.A && !input.B)
            { input.Left = toward < 0; input.Right = toward > 0; }
            if (previous.A) input.A = false;
            if (previous.B) input.B = false;
            if (previous.Jump) input.Jump = false;
            return input;
        }

        float DesiredDistance(Fighter f, Fighter opponent)
        {
            if (level <= 3)
            {
                // Personality is chosen before the match, not derived from the club.
                // Melee-only ranged personalities occasionally commit to an approach.
                bool meleeExcursion = f.Club.StrongMove.Projectile == null && clock % 4.8f > 2.8f;
                if (personality == CpuPersonality.Rushdown || meleeExcursion) return 78;
                return personality == CpuPersonality.KeepDistance ? 300 : 455;
            }
            if (f.Club.StrongMove.Projectile == null) return f.Club.LightMoves[0].Reach + 15;
            var projectile = f.Club.StrongMove.Projectile;
            float projectileSpeed = projectile.Speed;
            // A slow flask wants space to throw, a fast serve can threaten from farther.
            float ideal = Math.Min(500, Math.Max(320, projectileSpeed * .60f));
            bool bow = projectile.Kind == "arrow";
            if (bow) ideal = Math.Min(620, Math.Min(projectile.Range, projectile.Speed * projectile.Life) * .70f);
            if (opponent.Club.Stats.Speed > f.Club.Stats.Speed * 1.4f) ideal += 70;
            if (f.BCooldown > .25f && !bow) return f.Club.LightMoves[0].Reach + 18;
            // Curved shots have height-dependent safe ranges. In particular a flask
            // can pass above a grounded fighter at its otherwise ideal distance.
            // Use the same finite trajectory predicate as the combat simulation.
            float best = f.Club.LightMoves[0].Reach + 18, score = float.MaxValue;
            for (float candidate = 220; candidate <= World.Width - 150; candidate += 12)
            {
                if (!Battle.CanStrongReachAtDistance(f, candidate, 0)) continue;
                float error = Math.Abs(candidate - ideal);
                if (error < score) { best = candidate; score = error; }
            }
            return best;
        }

        static bool RetreatRoom(Fighter f, int toward)
        { return toward > 0 ? f.X > 86 : f.X < World.Width - 86; }

        bool StrongWorthUsing(Fighter f, Fighter op, float distance)
        {
            if (f.Club.StrongMove.Projectile == null) return distance > f.Club.LightMoves[0].Reach + 18;
            if (level <= 3)
            {
                if (personality == CpuPersonality.Rushdown) return distance < f.Club.LightMoves[0].Reach + 170;
                return distance > f.Club.LightMoves[0].Reach + 70;
            }
            bool enemyRecovering = op.Attack != null && op.Attack.Elapsed > op.Attack.Move.Startup;
            float enemyApproach = op.Vx * (f.X > op.X ? 1 : -1);
            float safeDistance = f.Club.LightMoves[0].Reach + 95 + Math.Max(0, enemyApproach) * f.Club.StrongMove.Startup;
            if (f.Club.StrongMove.Projectile.Kind == "arrow") return distance > safeDistance && op.Y < 130;
            return distance > safeDistance && (projectileMemory <= 0 || enemyRecovering)
                && op.Y < 130;
        }
    }
}
