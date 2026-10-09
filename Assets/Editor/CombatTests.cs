#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using ClubClash;
using UnityEditor;
using UnityEngine;

public static class CombatTests
{
    public static string LastReport { get; private set; }
    static readonly List<string> results = new List<string>();
    static int failures;

    [MenuItem("Club Clash/Run combat checks")]
    static void MenuRun() { RunAll(); }

    public static bool RunAll()
    {
        results.Clear(); failures = 0;
        Check("mobile_layout_spacing_controls_names_and_sizes", MobileUiChecks.Run);
        Check("catalog_ratings_and_reach_timing", CatalogBalance);
        Check("boxing_closes_soccer_in_two_shots", BoxingApproach);
        Check("ranged_cooldown_preserves_other_actions", CooldownActions);
        Check("one_hit_per_attack_and_no_hit_outside_reach", MeleeGeometry);
        Check("guard_item_front_back_and_break", GuardRules);
        Check("buffered_combo_and_air_attack", ComboAndAir);
        Check("rounds_draw_and_stationary_practice_teacher", RoundRules);
        Check("pause_and_seed_determinism", Determinism);
        Check("home_stage_all_abilities_1_3_without_range_or_poison_extension", HomeStage);
        Check("ballistic_launch_arcs_initial_and_medium_hits_finite_range", Trajectories);
        Check("science_poison_seven_ticks_guard_refresh_round_reset", PoisonRules);
        Check("science_bottle_jump_and_movement_dodge", BottleDodge);
        Check("swept_projectile_cannot_tunnel_through_fighter", SweptProjectile);
        Check("practice_live_switch_preserves_session_and_clears_actions", PracticeSwitch);
        Check("practice_teacher_poison_seven_ticks_never_dies_or_moves", PracticePoison);
        Check("five_cpu_levels_fair_stats_and_automatic_seeded_strategies", CpuLevels);
        Check("beginner_cpu_hit_gaps_and_slow_approach", BeginnerCpu);
        Check("automatic_personalities_change_plan_and_hard_cpu_uses_club_strengths", CpuStrategies);
        Check("hard_cpu_target_range_matches_finite_trajectory_all_clubs_stages", CpuRanges);
        Check("archery_cpu_retreats_during_cooldown_and_counters_from_bow_range", ArcheryCpu);
        Check("hard_cpu_counters_ranged_spam_and_kiting_across_roster", CpuAntiRanged);
        Check("home_club_first_slot_weak_baseline_and_no_stage_bonus", HomeClub);
        Check("bicycle_charge_distance_startup_whiff_reverse_and_boundary", BicycleTravel);
        Check("bicycle_one_hit_guard_and_stationary_teacher", BicycleContact);
        Check("bicycle_pause_interrupt_ko_and_air_kick", BicycleSafety);
        LastReport = "{\"passed\":" + (results.Count - failures) + ",\"failed\":" + failures
            + ",\"checks\":[" + string.Join(",", results.ToArray()) + "]}";
        if (failures == 0) Debug.Log("CLUB_CLASH_COMBAT_TESTS " + LastReport);
        else Debug.LogError("CLUB_CLASH_COMBAT_TESTS " + LastReport);
        return failures == 0;
    }

    static void Check(string name, Action test)
    {
        try { test(); results.Add("{\"name\":\"" + name + "\",\"passed\":true}"); }
        catch (Exception e)
        {
            failures++;
            results.Add("{\"name\":\"" + name + "\",\"passed\":false,\"error\":\""
                + e.Message.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"}");
        }
    }

    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Battle Ready(string p1 = "boxing", string p2 = "soccer", BattleMode mode = BattleMode.Local,
        StageId stage = StageId.Classroom)
    {
        var battle = new Battle(new BattleOptions { P1 = p1, P2 = p2, Mode = mode, Seed = 42, Stage = stage });
        Advance(battle, 3.05f);
        Assert(battle.Phase == BattlePhase.Fight, "Countdown did not finish");
        return battle;
    }

    static void Advance(Battle b, float duration, InputFrame a = default(InputFrame), InputFrame c = default(InputFrame))
    {
        for (float elapsed = 0; elapsed < duration - .00001f;)
        {
            float dt = Math.Min(1f / 120, duration - elapsed); b.Update(dt, a, c); elapsed += dt;
        }
    }

    static void CatalogBalance()
    {
        Assert(Catalog.Clubs.Count == 19, "Expected 19 clubs");
        foreach (var club in Catalog.Clubs)
        {
            Assert(club.PowerRating >= 1 && club.PowerRating <= 10 && club.SpeedRating >= 1 && club.SpeedRating <= 10
                && club.GuardRating >= 1 && club.GuardRating <= 10, club.Id + " rating is outside 1..10");
            Assert(Math.Abs(club.Stats.Speed - (100 + club.SpeedRating * 32)) < .001f, "Speed rating is not reflected in gameplay");
            if (club.StrongMove.Projectile != null)
                Assert(club.StrongMove.Cooldown >= 1.65f && club.StrongMove.Startup >= .34f, club.Id + " fires too quickly");
            foreach (var other in Catalog.Clubs)
                if (club.LightMoves[0].Reach < other.LightMoves[0].Reach)
                    Assert(club.LightMoves[0].Total < other.LightMoves[0].Total
                        && club.LightMoves[0].Startup < other.LightMoves[0].Startup,
                        club.Id + " should act faster than longer reach " + other.Id);
        }
        Assert(Catalog.Get("boxing").Stats.Speed > Catalog.Get("soccer").Stats.Speed, "Boxer movement must be faster");
        Assert(Catalog.Get("kendo").Stats.GuardScale < Catalog.Get("boxing").Stats.GuardScale, "Item guard must resist more damage");
        var original = Catalog.Get("soccer");
        var loadout = original.Copy();
        loadout.Stats.Speed += 17;
        loadout.LightMoves[0].Damage += 3;
        loadout.StrongMove.Damage += 7;
        loadout.StrongMove.Projectile.Speed += 111;
        var battle = new Battle(new BattleOptions { P1 = "soccer", P2 = "soccer", P1Definition = loadout, Stage = StageId.Classroom });
        Assert(battle.Fighters[0].Club.StrongMove.Damage == original.StrongMove.Damage + 7
            && battle.Fighters[0].Club.StrongMove.Projectile.Speed == original.StrongMove.Projectile.Speed + 111
            && battle.Fighters[0].Club.Stats.Speed == original.Stats.Speed + 17,
            "Per-match loadout override was not applied to player one");
        Assert(battle.Fighters[1].Club.StrongMove.Damage == original.StrongMove.Damage
            && battle.Fighters[1].Club.StrongMove.Projectile.Speed == original.StrongMove.Projectile.Speed
            && battle.Fighters[1].Club.LightMoves[0].Damage == original.LightMoves[0].Damage,
            "Player one loadout leaked to the other fighter");
        float expected = battle.Fighters[0].Club.StrongMove.Damage;
        loadout.StrongMove.Damage += 99;
        Assert(battle.Fighters[0].Club.StrongMove.Damage == expected, "Input definition mutated an active fighter");
        battle.Fighters[0].Club.StrongMove.Projectile.Speed += 999;
        battle.Fighters[0].Club.LightMoves[0].Damage += 999;
        Assert(Catalog.Get("soccer").StrongMove.Projectile.Speed == 620
            && battle.Fighters[1].Club.StrongMove.Projectile.Speed == 620
            && battle.Fighters[1].Club.LightMoves[0].Damage == original.LightMoves[0].Damage,
            "Fighter changes mutated the shared catalog or opponent");
        battle.Restart();
        Assert(battle.Fighters[0].Club.StrongMove.Damage == expected
            && battle.Fighters[0].Club.StrongMove.Projectile.Speed == original.StrongMove.Projectile.Speed + 111,
            "Restart did not restore the isolated loadout snapshot");
        Assert(Catalog.Get("science").Stats.Speed < Catalog.Get("golf").Stats.Speed,
            "Science must be the slowest walking club");
        Assert(Catalog.Get("volleyball").LightMoves[0].Kind == "ball" && Catalog.Get("handball").LightMoves[0].Kind == "ball"
            && Catalog.Get("science").LightMoves[0].Kind == "flask", "Club light attacks must use their requested equipment");
    }

    static void BoxingApproach()
    {
        var b = Ready(); int shots = 0; float duration = 0;
        bool wasB = false;
        while (duration < 4 && b.Fighters[1].X - b.Fighters[0].X > 105)
        {
            bool fire = !wasB && b.Fighters[1].BCooldown <= 0 && b.Fighters[1].Attack == null;
            b.Update(1f / 120, new InputFrame { Right = true }, new InputFrame { B = fire });
            foreach (var e in b.Events) if (e.Type == "projectile") shots++;
            wasB = fire; duration += 1f / 120;
        }
        Assert(b.Fighters[1].X - b.Fighters[0].X <= 105, "Boxer failed to close the initial 520-pixel gap");
        Assert(shots >= 1 && shots <= 2, "Boxer needed " + shots + " soccer shots before closing");
        Assert(b.Fighters[0].Hp > 0, "Boxer was defeated before entering melee range");
        Debug.Log("BOXER_APPROACH seconds=" + duration.ToString("F3") + " shots=" + shots + " hp=" + b.Fighters[0].Hp);
    }

    static void CooldownActions()
    {
        var b = Ready("soccer", "boxing");
        b.Update(1f / 120, new InputFrame { B = true }); Advance(b, 1.02f);
        var f = b.Fighters[0];
        Assert(f.Attack == null && f.BCooldown > .5f, "Cooldown should outlast attack recovery");
        float x = f.X; Advance(b, .15f, new InputFrame { Right = true });
        Assert(f.X > x + 15, "Remaining B cooldown blocked movement");
        b.Update(1f / 120, new InputFrame { Guard = true });
        Assert(f.Guard, "Remaining B cooldown blocked guard");
        b.Update(1f / 120, new InputFrame { A = true });
        Assert(f.Attack != null && f.Attack.Button == "a", "Remaining B cooldown blocked light attack");
        Advance(b, .4f); b.Update(1f / 120, new InputFrame { B = true });
        Assert(f.Attack == null, "Strong move restarted before cooldown ended");
        Advance(b, .6f); b.Update(1f / 120, new InputFrame { B = true });
        Assert(f.Attack != null && f.Attack.Button == "b", "Strong move did not become available after cooldown");
    }

    static void MeleeGeometry()
    {
        var b = Ready(); b.Fighters[0].X = 400; b.Fighters[1].X = 470;
        float damage = Catalog.Get("boxing").LightMoves[0].Damage;
        Advance(b, .6f, new InputFrame { A = true });
        Assert(b.Fighters[1].Hp == 120 - damage, "Held light attack should deal exactly one hit");
        b = Ready(); b.Fighters[0].X = 400; b.Fighters[1].X = 560;
        Advance(b, .4f, new InputFrame { A = true });
        Assert(b.Fighters[1].Hp == 120, "Boxing jab hit outside its reach");
        b = Ready("kendo", "boxing"); b.Fighters[0].X = 400; b.Fighters[1].X = 560;
        Advance(b, .5f, new InputFrame { A = true });
        Assert(b.Fighters[1].Hp < 120, "Long kendo strike should reach the same distance");
        b = Ready("boxing", "boxing"); b.Fighters[0].X = 400; b.Fighters[1].X = 470;
        b.Fighters[0].Hp = b.Fighters[1].Hp = damage;
        Advance(b, .2f, new InputFrame { A = true }, new InputFrame { A = true });
        Assert(b.Fighters[0].Hp == 0 && b.Fighters[1].Hp == 0 && b.RoundWinner == -1,
            "Equal-frame attacks should trade and permit a double-KO draw");
    }

    static float GuardedDamage(string defender, bool backwards)
    {
        var b = Ready("boxing", defender); b.Fighters[0].X = 400; b.Fighters[1].X = 480;
        b.Fighters[1].Facing = backwards ? 1 : -1;
        Advance(b, .14f, new InputFrame { A = true }, new InputFrame { Guard = true });
        return 120 - b.Fighters[1].Hp;
    }

    static void GuardRules()
    {
        float itemDamage = GuardedDamage("kendo", false), bodyDamage = GuardedDamage("boxing", false);
        Assert(itemDamage < bodyDamage, "Kendo item guard should take less chip damage than boxing guard");
        Assert(GuardedDamage("kendo", true) == Catalog.Get("boxing").LightMoves[0].Damage, "Guard blocked a strike from behind");
        var b = Ready("boxing", "soccer"); b.Fighters[0].X = 400; b.Fighters[1].X = 475; b.Fighters[1].Stamina = 1;
        Advance(b, .14f, new InputFrame { A = true }, new InputFrame { Guard = true });
        Assert(b.Fighters[1].GuardBroken > 0 && !b.Fighters[1].Guard, "Depleted guard did not break");
        Assert(b.Fighters[1].Hp < 120 - 2, "Guard break should expose full attack damage");
    }

    static void ComboAndAir()
    {
        var b = Ready();
        b.Update(1f / 120, new InputFrame { A = true }); Advance(b, .07f);
        b.Update(1f / 120, new InputFrame { A = true }); Advance(b, .14f);
        Assert(b.Fighters[0].Attack != null && b.Fighters[0].Attack.Combo == 1, "Buffered second combo strike did not start");
        b = Ready(); b.Fighters[0].X = 400; b.Fighters[1].X = 495;
        b.Update(1f / 120, new InputFrame { Jump = true }); Advance(b, .06f);
        b.Update(1f / 120, new InputFrame { B = true });
        Assert(b.Fighters[0].Attack != null && b.Fighters[0].Attack.Air, "Jumping strong attack did not select air move");
        Advance(b, .2f);
        Assert(b.Fighters[1].Hp < 120, "Air attack missed overlapping standing hurtbox");
    }

    static void RoundRules()
    {
        var b = Ready(); b.Fighters[1].Hp = 0; b.Update(1f / 120, InputFrame.Empty);
        Assert(b.Phase == BattlePhase.RoundOver && b.Wins[0] == 1 && b.NextRound(), "First win should advance to another round");
        Advance(b, 3.05f); b.Fighters[1].Hp = 0; b.Update(1f / 120, InputFrame.Empty);
        Assert(b.Phase == BattlePhase.MatchOver && b.Winner == 0 && b.Wins[0] == 2, "Two round wins should finish the match");
        b.Restart(); Assert(b.Round == 1 && b.Wins[0] == 0, "Restart should reset the score");
        b = Ready(); Advance(b, 60.1f);
        Assert(b.Phase == BattlePhase.RoundOver && b.RoundWinner == -1 && b.Wins[0] == 0 && b.Wins[1] == 0, "Equal-HP timeout should draw");
        b = Ready("boxing", "soccer", BattleMode.Practice);
        b.Fighters[0].X = 790; Advance(b, .4f, new InputFrame { A = true });
        Assert(b.Fighters[1].ClubId == "teacher" && b.PracticeDamage > 0 && b.PracticeHits == 1,
            "Practice must attack the hidden teacher and record actual damage");
        Advance(b, 64, default(InputFrame), new InputFrame { Left = true, Jump = true, A = true, B = true });
        Assert(b.Fighters[1].X == 870 && b.Fighters[1].Y == 0 && b.Fighters[1].Attack == null
            && b.Fighters[1].Hp == b.Fighters[1].MaxHp && b.Phase == BattlePhase.Fight && b.Time == 60,
            "Teacher must be stationary, ignore P2 input, and retain the untimed fight");
    }

    static void Near(float actual, float expected, string message)
    { Assert(Math.Abs(actual - expected) < .001f, message + " expected=" + expected + " actual=" + actual); }

    static void HomeClub()
    {
        Assert(Catalog.Clubs[0].Id == "home" && Catalog.Clubs[0].Name == "帰宅部", "Home club must be the first playable slot");
        var club = Catalog.Get("home");
        Assert(club.PowerRating == 3 && club.SpeedRating == 4 && club.GuardRating == 3
            && club.LightMoves[0].Kind == "punch" && club.LightMoves[1].Kind == "kick"
            && club.StrongMove.Kind == "bike" && club.StrongMove.Projectile == null,
            "Home club must remain a modest punch/kick fighter with a self-mounted bicycle");
        foreach (StageId stage in new[] { StageId.Ground, StageId.Classroom, StageId.Gym })
        {
            var fighter = new Fighter(club, 0, stage);
            Assert(!fighter.HomeAdvantage && fighter.StatMultiplier == 1 && fighter.MaxHp == 120 && fighter.MaxStamina == 100,
                "Home club gained an unintended stage bonus at " + stage);
            Near(fighter.Club.Stats.Speed, club.Stats.Speed, "Home club stage speed");
            Near(fighter.Club.Stats.Jump, club.Stats.Jump, "Home club stage jump");
            Near(fighter.Club.Stats.GuardScale, club.Stats.GuardScale, "Home club stage guard");
            Near(fighter.Club.StrongMove.Damage, club.StrongMove.Damage, "Home club stage damage");
            Near(fighter.Club.StrongMove.Cooldown, club.StrongMove.Cooldown, "Home club stage cooldown");
        }
    }

    static void BicycleTravel()
    {
        var b = Ready("home", "boxing"); b.Fighters[0].X = 100; b.Fighters[1].X = 1244;
        b.Fighters[0].Vx = 200;
        Advance(b, .28f, new InputFrame { B = true });
        var attack = b.Fighters[0].Attack;
        Assert(attack != null && !b.Fighters[0].IsBicycling && b.Fighters[0].X == 100, "Bicycle moved during mounting startup");
        Advance(b, .16f); Assert(b.Fighters[0].IsBicycling && b.Fighters[0].X > 190,
            "Bicycle must carry the fighter horizontally after startup");
        Advance(b, 1.4f);
        Near(b.Fighters[0].X, 740, "Whiff bicycle actor travel must stop at exactly half-screen distance");
        Assert(attack.ChargeTravel <= 640.001f && attack.BikeEnded && !b.Fighters[0].IsBicycling
            && b.Projectiles.Count == 0 && b.Fighters[1].Hp == 120, "Whiff bicycle failed to disappear or spawned a detached projectile");
        float x = b.Fighters[0].X; Advance(b, .6f); Near(b.Fighters[0].X, x, "Finished bicycle left residual velocity");
        b = Ready("home", "boxing"); b.Fighters[0].X = 1100; b.Fighters[1].X = 36;
        Advance(b, 1.8f, new InputFrame { B = true });
        Near(b.Fighters[0].X, 460, "Bicycle reverse travel");
        foreach (bool right in new[] { true, false })
        {
            b = Ready("home", "boxing"); b.Fighters[0].X = right ? 1150 : 100;
            b.Fighters[1].X = right ? 1244 : 36; b.Fighters[1].Y = 600; b.Fighters[1].Grounded = false;
            Advance(b, .5f, new InputFrame { B = true });
            Near(b.Fighters[0].X, right ? 1244 : 36, "Bicycle screen boundary");
            Assert(!b.Fighters[0].IsBicycling && b.Fighters[0].Vx == 0, "Wall did not stop and remove the bicycle");
        }
    }

    static void BicycleContact()
    {
        float damage = Catalog.Get("home").StrongMove.Damage;
        foreach (bool guard in new[] { false, true })
        {
            var b = Ready("home", "boxing"); int contacts = 0, bikeEnds = 0;
            float stoppedX = -1;
            for (int frame = 0; frame < 240; frame++)
            {
                b.Update(1f / 120, new InputFrame { B = frame == 0 }, new InputFrame { Guard = guard });
                foreach (var e in b.Events)
                {
                    if ((e.Type == "hit" || e.Type == "block") && e.Owner == 0)
                    { contacts++; stoppedX = b.Fighters[0].X; Assert(!b.Fighters[0].IsBicycling, "Contact did not remove bicycle immediately"); }
                    if (e.Type == "bikeend" && e.Owner == 0) bikeEnds++;
                }
            }
            float expected = guard ? Math.Max(1, (float)Math.Round(damage * b.Fighters[1].Club.Stats.GuardScale)) : damage;
            Near(b.Fighters[1].Hp, 120 - expected, "Bicycle contact must follow ordinary damage/guard rules");
            Assert(contacts == 1 && bikeEnds == 1 && stoppedX >= 350 && stoppedX < 990,
                "Bicycle should contact once and disappear before its maximum charge distance");
            Near(b.Fighters[0].X, stoppedX, "Bicycle kept pushing after contact");
        }
        var practice = new Battle(new BattleOptions { P1 = "home", Mode = BattleMode.Practice });
        Advance(practice, 2.5f, new InputFrame { B = true });
        Assert(practice.PracticeHits == 1 && practice.PracticeDamage == damage && practice.Fighters[1].X == 870
            && practice.Fighters[1].Y == 0 && practice.Fighters[1].Hp == 120 && !practice.Fighters[0].IsBicycling,
            "Bicycle must hit the teacher once without moving or defeating him");
    }

    static void BicycleSafety()
    {
        var b = Ready("home", "boxing");
        Advance(b, .38f, new InputFrame { B = true }); Assert(b.Fighters[0].IsBicycling, "Bicycle test setup did not enter charge");
        var attack = b.Fighters[0].Attack; float x = b.Fighters[0].X, time = b.Time, distance = attack.ChargeTravel, cooldown = b.Fighters[0].BCooldown;
        b.SetPaused(true); Advance(b, 2);
        Assert(b.Fighters[0].IsBicycling && b.Fighters[0].X == x && b.Time == time
            && attack.ChargeTravel == distance && b.Fighters[0].BCooldown == cooldown, "Pause did not freeze the mounted charge");
        b.SetPaused(false); Advance(b, .04f); Assert(b.Fighters[0].X > x, "Bicycle failed to resume after pause");
        foreach (bool lethal in new[] { false, true })
        {
            b = Ready("home", "boxing"); Advance(b, .36f, new InputFrame { B = true });
            b.Fighters[1].X = b.Fighters[0].X - 65; b.Fighters[1].Facing = 1;
            if (lethal) b.Fighters[0].Hp = 1;
            Advance(b, .09f, InputFrame.Empty, new InputFrame { A = true });
            Assert(!b.Fighters[0].IsBicycling && b.Fighters[0].Attack == null,
                "Normal enemy attack must interrupt the bicycle");
            Assert(lethal ? b.Fighters[0].Hp == 0 && b.Phase == BattlePhase.RoundOver : b.Fighters[0].Hp < 120,
                "Bicycle granted unintended armor against damage or KO");
        }
        b = Ready("home", "boxing"); b.Update(1f / 120, new InputFrame { Jump = true }); Advance(b, .10f);
        b.Update(1f / 120, new InputFrame { B = true });
        Assert(b.Fighters[0].Attack != null && b.Fighters[0].Attack.Air && b.Fighters[0].Attack.Move.Kind == "kick"
            && !b.Fighters[0].IsBicycling && b.Fighters[0].Attack.Move.ChargeDistance == 0, "Air strong must remain a kick without a flying bicycle");
    }

    static void PracticeSwitch()
    {
        var b = new Battle(new BattleOptions { P1 = "science", P2 = "boxing", Mode = BattleMode.Practice,
            Stage = StageId.Classroom });
        Assert(b.Phase == BattlePhase.Fight && b.Countdown == 0 && b.Fighters[1].ClubId == "teacher", "Practice startup");
        b.Fighters[0].X = 430;
        Advance(b, 2.4f, new InputFrame { B = true });
        Assert(b.PracticeDamage > 0 && b.Fighters[1].IsPoisoned, "Bottle must damage and poison practice target");
        float total = b.PracticeDamage;
        Assert(b.SetPracticeClub("swimming"), "Valid practice club switch rejected");
        Assert(b.Fighters[0].ClubId == "swimming" && b.Fighters[0].X == 430 && b.Fighters[0].BCooldown == 0
            && b.Fighters[0].Attack == null && !b.Fighters[1].IsPoisoned && b.Projectiles.Count == 0
            && b.PracticeDamage == total && b.Stage == StageId.Classroom && b.Phase == BattlePhase.Fight,
            "Practice switch must preserve session and clear in-flight and buffered combat state");
        foreach (var club in Catalog.Clubs)
        {
            Assert(b.SetPracticeClub(club.Id), "Switch missing " + club.Id);
            b.Fighters[0].X = 805;
            Advance(b, .40f, new InputFrame { A = true });
            Assert(b.Fighters[1].X == 870 && b.Fighters[1].Y == 0 && b.Fighters[1].Hp == 120,
                "Teacher moved or died under " + club.Id);
        }
        Assert(!b.SetPracticeClub("teacher") && !b.SetPracticeClub("missing") && b.PracticeHits >= 19,
            "Only actual clubs may be selected for practice");
        Assert(!Ready().SetPracticeClub("soccer"), "Competitive battle accepted practice-only switch");
    }

    static void CpuLevels()
    {
        for (int i = 1; i <= 5; i++)
        foreach (var club in Catalog.Clubs)
        foreach (StageId stage in new[] { StageId.Ground, StageId.Classroom, StageId.Gym })
        {
            var b = new Battle(new BattleOptions { P1 = "boxing", P2 = club.Id, Difficulty = (CpuDifficulty)i,
                Stage = stage, Personality = CpuPersonality.KeepDistance, Seed = 892 });
            var reference = new Fighter(club, 1, stage);
            Assert((int)b.Difficulty == i && b.EffectiveCpuPersonality != CpuPersonality.Random,
                "CPU failed to select an automatic strategy");
            Near(b.Fighters[1].MaxHp, reference.MaxHp, "CPU must not gain hidden health");
            Near(b.Fighters[1].Club.Stats.Speed, reference.Club.Stats.Speed, "CPU must not gain hidden speed");
            Near(b.Fighters[1].Club.Stats.GuardScale, reference.Club.Stats.GuardScale, "CPU guard cheat");
            Near(b.Fighters[1].Club.StrongMove.Damage, reference.Club.StrongMove.Damage, "CPU damage cheat");
            if (i >= 4)
                Assert(b.EffectiveCpuPersonality == (club.StrongMove.Projectile == null ? CpuPersonality.Rushdown : CpuPersonality.Ranged),
                    "Hard CPU ignored " + club.Id + " strength");
        }
        Assert((int)new Battle(new BattleOptions { Difficulty = (CpuDifficulty)900 }).Difficulty == 5,
            "Difficulty must clamp to five");
        for (int level = 1; level <= 3; level++)
        {
            var selected = new HashSet<CpuPersonality>();
            for (uint seed = 1; seed <= 32; seed++)
            {
                var reference = new Battle(new BattleOptions { P2 = "boxing", Difficulty = (CpuDifficulty)level, Seed = seed });
                selected.Add(reference.EffectiveCpuPersonality);
                foreach (var club in Catalog.Clubs)
                {
                    var b = new Battle(new BattleOptions { P2 = club.Id, Difficulty = (CpuDifficulty)level,
                        Personality = CpuPersonality.KeepDistance, Seed = seed });
                    Assert(b.EffectiveCpuPersonality == reference.EffectiveCpuPersonality && b.Personality == b.EffectiveCpuPersonality,
                        "Automatic low-level personality depended on club or legacy caller selection");
                    b.Restart(); Assert(b.EffectiveCpuPersonality == reference.EffectiveCpuPersonality,
                        "Restart changed the seeded automatic strategy");
                }
            }
            Assert(selected.Count == 3 && !selected.Contains(CpuPersonality.Random),
                "Automatic match seeds did not produce three distinct personalities");
        }
    }

    static void PracticePoison()
    {
        var b = new Battle(new BattleOptions { P1 = "science", Mode = BattleMode.Practice, Stage = StageId.Classroom });
        b.Fighters[0].X = 430; int ticks = 0;
        for (int frame = 0; frame < 1200; frame++)
        {
            b.Update(1f / 120, new InputFrame { B = frame == 0 });
            var teacher = b.Fighters[1];
            Assert(!float.IsNaN(teacher.Hp) && !float.IsInfinity(teacher.Hp) && teacher.Hp == 120
                && teacher.X == 870 && teacher.Y == 0 && teacher.Vx == 0 && teacher.Vy == 0
                && teacher.State != "down" && b.Phase == BattlePhase.Fight,
                "Practice teacher became nonfinite, moved or died during poison");
            foreach (var e in b.Events) if (e.Type == "poisontick" && e.Target == 1) ticks++;
        }
        Assert(ticks == 7 && !b.Fighters[1].IsPoisoned, "Teacher poison must end after the same seven ticks as combat");
        Near(b.PracticeDamage, (8 + 2 * 7) * 1.3f, "Practice must record direct plus poison damage without dropping HP");
    }

    static void BeginnerCpu()
    {
        var b = new Battle(new BattleOptions { P1 = "boxing", P2 = "boxing", Mode = BattleMode.Cpu,
            Difficulty = CpuDifficulty.Level1, Stage = StageId.Classroom, Seed = 1 });
        Advance(b, 3.05f); float start = b.Fighters[1].X;
        Advance(b, 1); Assert(start - b.Fighters[1].X < 230, "Level 1 must visibly approach more slowly");
        b.Fighters[0].X = 36; b.Fighters[1].X = 104;
        float clock = 0, previousHit = -100; int hits = 0;
        for (int i = 0; i < 1200; i++)
        {
            b.Update(1f / 120, InputFrame.Empty); clock += 1f / 120;
            foreach (var e in b.Events) if (e.Type == "hit" && e.Owner == 1)
            {
                Assert(clock - previousHit >= .75f, "Beginner corner hits must have a readable gap");
                previousHit = clock; hits++;
            }
        }
        Assert(hits > 0 && hits <= 9 && b.Fighters[0].Hp > 0, "Beginner should engage without an endless corner lock");
    }

    static void CpuStrategies()
    {
        float[] x = new float[2]; int[] shots = new int[2];
        for (int i = 0; i < 2; i++)
        {
            var b = new Battle(new BattleOptions { P1 = "boxing", P2 = "soccer", Mode = BattleMode.Cpu,
                Difficulty = CpuDifficulty.Level3, Stage = StageId.Classroom, Seed = i == 0 ? 1u : 8u });
            Assert(b.EffectiveCpuPersonality == (i == 0 ? CpuPersonality.Rushdown : CpuPersonality.Ranged),
                "Fixture seeds did not select the expected automatic personalities");
            Advance(b, 3.01f);
            for (int frame = 0; frame < 120; frame++)
            {
                b.Update(1f / 120, new InputFrame { Guard = true });
                foreach (var e in b.Events) if (e.Type == "projectile" && e.Owner == 1) shots[i]++;
            }
            x[i] = b.Fighters[1].X;
        }
        Assert(x[0] < x[1] - 90 && shots[0] == 0 && shots[1] > 0,
            "Automatic rushdown and ranged personalities did not visibly change the same club's plan");
        int meleeAttacks = 0, rangedAttacks = 0;
        foreach (string id in new[] { "boxing", "science" })
        {
            var b = new Battle(new BattleOptions { P1 = "soccer", P2 = id, Mode = BattleMode.Cpu,
                Difficulty = CpuDifficulty.Level4, Stage = StageId.Ground, Seed = 29 });
            Advance(b, 3.01f);
            for (int frame = 0; frame < 660 && b.Phase == BattlePhase.Fight; frame++)
            {
                b.Update(1f / 120, InputFrame.Empty);
                foreach (var e in b.Events) if (e.Type == "attack" && e.Owner == 1)
                {
                    if (id == "boxing" && e.Button == "a") meleeAttacks++;
                    if (id == "science" && e.Button == "b") rangedAttacks++;
                }
            }
        }
        Assert(meleeAttacks > 0 && rangedAttacks > 0, "Level4 must use melee boxing and science flask strengths");
    }

    static void CpuRanges()
    {
        for (int level = 4; level <= 5; level++)
        foreach (var club in Catalog.Clubs)
        foreach (StageId stage in new[] { StageId.Ground, StageId.Classroom, StageId.Gym })
        {
            var b = new Battle(new BattleOptions { P1 = "boxing", P2 = club.Id, Difficulty = (CpuDifficulty)level,
                Stage = stage, Seed = 901 });
            Advance(b, 3); b.Update(1f / 120, InputFrame.Empty);
            float desired = b.CpuTargetDistance;
            Assert(desired > 0 && !float.IsNaN(desired) && !float.IsInfinity(desired), "Invalid CPU target " + club.Id);
            if (club.StrongMove.Projectile != null)
            {
                Assert(desired >= 220 && Battle.CanStrongReachAtDistance(b.Fighters[1], desired, 0),
                    club.Id + " CPU chose a distance where its own shot cannot hit");
                b.Fighters[0].X = 36; b.Fighters[1].X = 1244;
                b.Fighters[1].Attack = null; b.Fighters[1].BCooldown = 0;
                int shots = 0;
                for (int frame = 0; frame < 30; frame++)
                {
                    b.Update(1f / 120, InputFrame.Empty);
                    foreach (var e in b.Events) if (e.Type == "projectile" && e.Owner == 1) shots++;
                }
                Assert(shots == 0 && b.Fighters[1].X < 1244, club.Id + " fired out of finite range rather than approaching");
            }
        }
    }

    static void ArcheryCpu()
    {
        for (int level = 4; level <= 5; level++)
        foreach (StageId stage in new[] { StageId.Ground, StageId.Classroom, StageId.Gym })
        {
            var b = new Battle(new BattleOptions { P1 = "soccer", P2 = "archery", Difficulty = (CpuDifficulty)level,
                Stage = stage, Seed = 724 });
            Advance(b, 3); b.Fighters[0].X = 420; b.Fighters[1].X = 760; b.Fighters[1].BCooldown = 1.2f;
            b.Update(1f / 120, new InputFrame { B = true });
            Assert(b.EffectiveCpuPersonality == CpuPersonality.Ranged && b.CpuTargetDistance >= 540
                && b.CpuInput.Right && !b.CpuInput.Left,
                "Bow CPU rushed a committed player shot instead of keeping its range");
            Advance(b, .40f);
            Assert(b.Fighters[1].X > 815, "Bow CPU did not retreat during cooldown");
            int arrows = 0; float distanceSum = 0; int samples = 0;
            for (int frame = 0; frame < 1200 && b.Phase == BattlePhase.Fight; frame++)
            {
                b.Update(1f / 120, new InputFrame { Guard = true });
                distanceSum += Math.Abs(b.Fighters[1].X - b.Fighters[0].X); samples++;
                foreach (var e in b.Events) if (e.Type == "projectile" && e.Owner == 1 && e.Kind == "arrow") arrows++;
            }
            Assert(arrows >= 2 && distanceSum / samples > 450,
                "Bow CPU failed to attack and maintain useful range after retreat");

            // The incoming serve is more than .48 seconds away but will arrive
            // during a bow attack's normal recovery. Do not start a doomed trade.
            var clear = new Battle(new BattleOptions { P1 = "tennis", P2 = "archery", Difficulty = (CpuDifficulty)level,
                Stage = stage, Seed = 724 });
            var threatened = new Battle(new BattleOptions { P1 = "tennis", P2 = "archery", Difficulty = (CpuDifficulty)level,
                Stage = stage, Seed = 724 });
            Advance(clear, 3); Advance(threatened, 3);
            foreach (var match in new[] { clear, threatened })
            { match.Fighters[0].X = 350; match.Fighters[1].X = 900; }
            var serve = threatened.Fighters[0].Club.StrongMove.Projectile;
            threatened.Projectiles.Add(new Projectile { Owner = 0, Kind = serve.Kind,
                X = 900 - serve.Speed * .60f, Y = serve.Height, Vx = serve.Speed, Vy = serve.Vy,
                Gravity = serve.Gravity, Radius = serve.Radius, Life = serve.Life, MaxRange = serve.Range,
                Damage = 24, GuardDamage = 31, Stun = .17f });
            clear.Update(1f / 120, InputFrame.Empty); threatened.Update(1f / 120, InputFrame.Empty);
            Assert(clear.Fighters[1].Attack != null && clear.Fighters[1].Attack.Move.Projectile != null
                && threatened.Fighters[1].Attack == null && !threatened.CpuInput.B,
                "Bow CPU committed to a counter-shot that the incoming serve would interrupt");
            int counters = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                threatened.Update(1f / 120, InputFrame.Empty);
                foreach (var e in threatened.Events) if (e.Type == "projectile" && e.Owner == 1) counters++;
            }
            Assert(counters > 0 && threatened.Fighters[1].Hp >= threatened.Fighters[1].MaxHp - 6,
                "Bow CPU failed to defend the serve and then counter with its own normal shot");
        }
    }

    static void CpuAntiRanged()
    {
        for (int difficulty = 4; difficulty <= 5; difficulty++)
        {
            int wins = 0, count = 0;
            foreach (string attacker in new[] { "soccer", "tennis", "science" })
            foreach (var defender in Catalog.Clubs)
            foreach (StageId stage in new[] { StageId.Ground, StageId.Classroom, StageId.Gym })
            for (int policy = 0; policy < 2; policy++)
            {
                var b = new Battle(new BattleOptions { P1 = attacker, P2 = defender.Id, Mode = BattleMode.Cpu,
                    Stage = stage, Difficulty = (CpuDifficulty)difficulty, Seed = (uint)(703 + policy * 419) });
                Advance(b, 3.01f);
                for (int frame = 0; frame < 9600 && b.Phase == BattlePhase.Fight; frame++)
                {
                    bool left = b.Fighters[0].X < b.Fighters[1].X;
                    b.Update(1f / 120, new InputFrame { B = frame % 2 == 0,
                        Left = policy == 1 && left, Right = policy == 1 && !left });
                }
                Assert(b.Phase != BattlePhase.Fight, "CPU ranged counter failed to finish " + defender.Id);
                if (b.RoundWinner == 1) wins++;
                count++;
            }
            Debug.Log("CPU_ANTI_RANGED level=" + difficulty + " cpuWins=" + wins + " rounds=" + count);
            Assert(wins >= count * (difficulty == 4 ? .85f : .93f),
                "Level " + difficulty + " should defeat simple ranged spam/kiting across clubs and stages: " + wins + "/" + count);
        }
    }

    static void HomeStage()
    {
        int ground = 0, classroom = 0, gym = 0;
        foreach (var club in Catalog.Clubs)
        {
            if (club.Id == "home") continue;
            if (club.HomeStage == StageId.Ground) ground++;
            else if (club.HomeStage == StageId.Classroom) classroom++;
            else gym++;
            StageId awayStage = club.HomeStage == StageId.Ground ? StageId.Classroom : StageId.Ground;
            var away = new Fighter(club, 0, awayStage); var home = new Fighter(club, 0, club.HomeStage);
            Assert(home.HomeAdvantage && !away.HomeAdvantage, club.Id + " home stage did not activate");
            Near(home.MaxHp, away.MaxHp * 1.3f, "Home HP"); Near(home.MaxStamina, away.MaxStamina * 1.3f, "Home stamina");
            Near(home.Club.Stats.Speed, away.Club.Stats.Speed * 1.3f, "Home speed");
            Near(home.Club.Stats.Jump, away.Club.Stats.Jump * 1.3f, "Home jump");
            Near(home.Club.Stats.GuardScale, away.Club.Stats.GuardScale / 1.3f, "Home damage reduction");
            Near(home.Club.Stats.GuardCost, away.Club.Stats.GuardCost / 1.3f, "Home guard efficiency");
            Assert(home.Club.PowerRating == club.PowerRating && home.Club.SpeedRating == club.SpeedRating
                && home.Club.GuardRating == club.GuardRating, "Home must preserve base ten-point ratings");
            for (int i = 0; i < 3; i++)
            {
                Near(home.Club.LightMoves[i].Damage, away.Club.LightMoves[i].Damage * 1.3f, "Home light damage");
                Near(home.Club.LightMoves[i].Startup, away.Club.LightMoves[i].Startup / 1.3f, "Home startup");
                Near(home.Club.LightMoves[i].Total, away.Club.LightMoves[i].Total / 1.3f, "Home recovery");
                Near(home.Club.LightMoves[i].Reach, away.Club.LightMoves[i].Reach, "Home must preserve reach");
            }
            Near(home.Club.StrongMove.Damage, away.Club.StrongMove.Damage * 1.3f, "Home strong damage");
            Near(home.Club.StrongMove.Cooldown, away.Club.StrongMove.Cooldown / 1.3f, "Home cooldown");
            Near(home.Club.StrongMove.Reach, away.Club.StrongMove.Reach, "Home strong reach");
            Assert(home.Club.StrongMove.PoisonTicks == away.Club.StrongMove.PoisonTicks, "Home must preserve poison duration");
            var p = away.Club.StrongMove.Projectile; var hp = home.Club.StrongMove.Projectile;
            if (p != null)
            {
                Near(hp.Range, p.Range, "Home must preserve ball range");
                float t = 400 / p.Speed, ht = 400 / hp.Speed;
                Near(hp.Height + hp.Vy * ht - .5f * hp.Gravity * ht * ht,
                    p.Height + p.Vy * t - .5f * p.Gravity * t * t, "Home must preserve spatial arc");
            }
        }
        Assert(ground == 7 && classroom == 5 && gym == 6, "Unexpected home stage distribution for established 18 clubs");
        Assert(Catalog.Get("swimming").HomeStage == StageId.Ground && Catalog.Get("tennis").HomeStage == StageId.Ground,
            "Swimming and tennis must use the outdoor stage");
        var b = Ready("soccer", "boxing", BattleMode.Local, StageId.Ground);
        b.Fighters[0].Stamina = b.Fighters[1].Stamina = 10;
        Advance(b, .1f);
        Near(b.Fighters[0].Stamina, 13.25f, "Home recovery"); Near(b.Fighters[1].Stamina, 12.5f, "Away recovery");
        b.Restart(); Near(b.Fighters[0].MaxHp, 156, "Restart should not compound home multipliers");
    }

    static Battle Neutral(string id)
    {
        var stage = Catalog.Get(id).HomeStage == StageId.Ground ? StageId.Classroom : StageId.Ground;
        return Ready(id, "boxing", BattleMode.Local, stage);
    }

    static BattleEvent FireUntilImpact(Battle b, bool guard, out float spawnY)
    {
        spawnY = -1;
        for (int i = 0; i < 600; i++)
        {
            b.Update(1f / 120, new InputFrame { B = i == 0 }, new InputFrame { Guard = guard });
            foreach (var e in b.Events)
            {
                if (e.Type == "projectile") spawnY = e.Y;
                if ((e.Type == "hit" || e.Type == "block") && e.Projectile && e.Target == 1) return e;
            }
        }
        return null;
    }

    static void Trajectories()
    {
        foreach (var club in Catalog.Clubs)
        {
            if (club.StrongMove.Projectile == null) continue;
            var b = Neutral(club.Id); float launch;
            var impact = FireUntilImpact(b, false, out launch);
            Assert(impact != null, club.Id + " projectile missed the initial 520-pixel gap");
            Assert(impact.Kind == club.StrongMove.Projectile.Kind, club.Id + " impact lost its visual kind");
            if (club.Id == "soccer")
            { Near(launch, 35, "Soccer launches low"); Assert(impact.Y > 50 && impact.Y < 180, "Soccer did not rise into body height"); }
            if (club.Id == "baseball" || club.Id == "handball")
            { Near(launch, 160, "Throw launches at head height"); Assert(impact.Y > 65 && impact.Y < 125, "Throw did not descend toward waist height"); }
            if (club.Id == "science")
                Assert(impact.Y >= 65 && impact.Y <= 150, "Science bottle must strike waist-to-chest height at the initial gap");
            if (club.Id == "volleyball" || club.Id == "tennis")
            {
                Near(launch, 190, "Serve launches overhead");
                Assert(club.StrongMove.Projectile.Vy < 0 && impact.Y < 160, "Serve should descend from above");
                Assert(club.StrongMove.Damage >= 24, "Overhead serve should have higher power");
                b = Neutral(club.Id); b.Fighters[0].X = 250; b.Fighters[1].X = 900;
                Assert(FireUntilImpact(b, false, out launch) != null, club.Id + " serve missed medium range");
            }
            b = Neutral(club.Id); b.Fighters[0].X = 80; b.Fighters[1].X = 1180;
            Advance(b, 4, new InputFrame { B = true });
            Assert(b.Fighters[1].Hp == b.Fighters[1].MaxHp && !b.Fighters[1].IsPoisoned,
                club.Id + " projectile reached the opposite far end");
            Assert(b.Projectiles.Count == 0, club.Id + " projectile did not expire");
        }
    }

    static int PoisonTicks(Battle b, float duration, bool guard)
    {
        int ticks = 0;
        for (float elapsed = 0; elapsed < duration - .00001f; elapsed += 1f / 120)
        {
            b.Update(1f / 120, InputFrame.Empty, new InputFrame { Guard = guard });
            foreach (var e in b.Events) if (e.Type == "poisontick" && e.Target == 1) ticks++;
        }
        return ticks;
    }

    static void PoisonRules()
    {
        float launch; var b = Neutral("science");
        Assert(FireUntilImpact(b, false, out launch) != null, "Bottle did not strike");
        Near(b.Fighters[1].Hp, 112, "Unguarded bottle direct damage");
        Assert(b.Fighters[1].PoisonTicksRemaining == 7, "Poison must begin with seven ticks");
        Assert(PoisonTicks(b, 7.05f, false) == 7, "Poison must tick exactly seven times");
        Near(b.Fighters[1].Hp, 98, "Poison must deal baseline two damage per tick");
        Assert(!b.Fighters[1].IsPoisoned && PoisonTicks(b, 1, false) == 0, "Poison did not expire after seven seconds");

        b = Neutral("science"); var guarded = FireUntilImpact(b, true, out launch);
        Assert(guarded != null && guarded.Type == "block" && guarded.Damage == 0, "Guard must prevent direct bottle damage");
        Near(b.Fighters[1].Hp, 120, "Guarded bottle dealt direct damage");
        Assert(b.Fighters[1].IsPoisoned, "Guard must still admit poison");
        Assert(PoisonTicks(b, 7.05f, true) == 7, "Guarded poison should still tick seven times");
        Near(b.Fighters[1].Hp, 106, "Guarded poison total damage");

        b = Neutral("science"); FireUntilImpact(b, true, out launch); PoisonTicks(b, .3f, true);
        Assert(FireUntilImpact(b, true, out launch) != null && b.Fighters[1].PoisonTicksRemaining == 7,
            "Second bottle must refresh poison to seven ticks");
        float before = b.Fighters[1].Hp;
        Assert(PoisonTicks(b, 7.05f, true) == 7, "Refresh must not create multiple ticking stacks");
        Near(b.Fighters[1].Hp, before - 14, "Refresh must preserve two damage per tick");

        b = Ready("science", "boxing", BattleMode.Local, StageId.Classroom);
        FireUntilImpact(b, true, out launch); before = b.Fighters[1].Hp;
        Assert(PoisonTicks(b, 7.05f, true) == 7, "Home poison duration should remain seven seconds");
        Near(b.Fighters[1].Hp, before - 18.2f, "Home poison damage should scale by 1.3");
        FireUntilImpact(b, true, out launch);
        b.Fighters[1].Hp = 0; b.Update(1f / 120, InputFrame.Empty);
        Assert(b.NextRound(), "Poison round should advance");
        Assert(!b.Fighters[0].IsPoisoned && !b.Fighters[1].IsPoisoned && b.Fighters[1].PoisonRemaining == 0,
            "Round reset must clear poison and its timer");
    }

    static void BottleDodge()
    {
        var b = Neutral("science"); bool jumped = false;
        for (int i = 0; i < 480; i++)
        {
            bool jump = false;
            foreach (var p in b.Projectiles) if (p.Kind == "flask" && p.X > 730 && !jumped) { jump = true; jumped = true; }
            b.Update(1f / 120, new InputFrame { B = i == 0 }, new InputFrame { Jump = jump });
        }
        Assert(jumped && b.Fighters[1].Hp == 120 && !b.Fighters[1].IsPoisoned, "A timed jump should evade the slow bottle arc");
        b = Neutral("science");
        Advance(b, 4, new InputFrame { B = true }, new InputFrame { Right = true });
        Assert(b.Fighters[1].Hp == 120 && !b.Fighters[1].IsPoisoned, "Moving outside bottle range should evade poison");
    }

    static void SweptProjectile()
    {
        var fast = Catalog.Get("soccer").Copy();
        fast.StrongMove.Startup = .01f; fast.StrongMove.Active = .05f; fast.StrongMove.Total = .1f;
        fast.StrongMove.Projectile.Speed = 50000; fast.StrongMove.Projectile.Vy = 0;
        fast.StrongMove.Projectile.Gravity = 0; fast.StrongMove.Projectile.Height = 100;
        fast.StrongMove.Projectile.Range = 900; fast.StrongMove.Projectile.Life = .1f;
        var b = new Battle(new BattleOptions { P1Definition = fast, P2 = "boxing", Mode = BattleMode.Local, Stage = StageId.Classroom });
        Advance(b, 3.05f); float launch;
        Assert(FireUntilImpact(b, false, out launch) != null && b.Fighters[1].Hp < 120,
            "Fast projectile crossed a fighter without a swept collision");
    }

    static void Determinism()
    {
        var a = new Battle(new BattleOptions { P1 = "boxing", P2 = "soccer", Mode = BattleMode.Cpu, Seed = 17 });
        var b = new Battle(new BattleOptions { P1 = "boxing", P2 = "soccer", Mode = BattleMode.Cpu, Seed = 17 });
        for (int i = 0; i < 1500; i++)
        {
            var input = new InputFrame { Right = i % 250 < 200, A = i % 23 == 0, B = i % 119 == 0, Guard = i % 197 > 180, Jump = i % 149 == 0 };
            a.Update(1f / 60, input); b.Update(1f / 60, input);
        }
        for (int i = 0; i < 2; i++)
            Assert(a.Fighters[i].X == b.Fighters[i].X && a.Fighters[i].Hp == b.Fighters[i].Hp
                && a.Fighters[i].Stamina == b.Fighters[i].Stamina, "Identical seed and inputs diverged");
        float x = a.Fighters[0].X, hp = a.Fighters[0].Hp, time = a.Time;
        a.SetPaused(true); a.Update(2, new InputFrame { Right = true, A = true });
        Assert(a.Fighters[0].X == x && a.Fighters[0].Hp == hp && a.Time == time, "Pause advanced the simulation");
    }
}
#endif
