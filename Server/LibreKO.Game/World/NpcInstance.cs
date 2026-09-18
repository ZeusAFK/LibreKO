using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;

using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public enum NpcState : byte
{
    Standing = 0,
    Moving = 1,
    Attacking = 2,   // Moving toward target
    Fighting = 3,    // In attack range, executing attacks
    Returning = 4,   // Returning to spawn after losing target
    Casting = 5,     // Casting a magic spell (waiting for cast time)
    Dead = 6,
    Healing = 7,     // Searching for / healing wounded allies
    Sleeping = 8
}

public class NpcInstance
{
    private readonly Lock _sync = new();

    public void WithLock(Action<NpcInstance> mutator)
    {
        using var scope = _sync.EnterScope();
        mutator(this);
    }

    public T WithLock<T>(Func<NpcInstance, T> reader)
    {
        using var scope = _sync.EnterScope();
        return reader(this);
    }

    public int UniqueId { get; set; }
    public int NpcId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte NpcType { get; set; }
    public byte Group { get; set; }
    public short Level { get; set; }
    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int MaxMp { get; set; }
    public int Mp { get; set; }
    public short Ac { get; set; }
    public short Attack1 { get; set; }
    public short Attack2 { get; set; }
    public int SellingGroup { get; set; }
    public short ModelId { get; set; }
    public short Size { get; set; }
    public int WeaponType1 { get; set; }
    public int WeaponType2 { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public byte ZoneId { get; set; }

    // Spawn (home) position for returning after combat or patrol
    public float SpawnX { get; set; }
    public float SpawnY { get; set; }
    public float SpawnZ { get; set; }
    public short Direction { get; set; }   // spawn facing in degrees (NpcPositions); 0 = unset → client varies it

    // Movement state
    public float TargetX { get; set; }
    public float TargetZ { get; set; }
    public bool IsMoving { get; set; }

    // Patrol/waypoint data
    public NpcMoveType MoveType { get; set; }
    public NpcMoveType InitMoveType { get; set; }
    public (short X, short Z)[] Waypoints { get; set; } = [];
    public short CurrentWaypoint { get; set; }

    // Combat/reward data
    public int GoldDrop { get; set; }
    public int Experience { get; set; }
    public short DropItemGroup { get; set; }
    public bool IsMonster { get; set; }

    // AI data (from K_NPC table)
    public byte ActType { get; set; }
    public short AttackDelay { get; set; }  // ms between attacks
    public short NpcSpeed { get; set; }     // movement delay ms
    public byte Speed1 { get; set; }        // normal speed
    public byte Speed2 { get; set; }        // combat speed
    public byte AttackRange { get; set; }   // attack range
    public byte SearchRange { get; set; }   // aggro detection range
    public byte TracingRange { get; set; }  // max chase range
    public short Bulk { get; set; }         // collision radius
    public short HitRate { get; set; }
    public short EvadeRate { get; set; }

    // Elemental resistances (from K_NPC table)
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short PoisonR { get; set; }
    public short CurseR { get; set; }

    // Magic attack data (from K_NPC table)
    public byte DirectAttack { get; set; }   // 0=normal, 1=long range, 2=magic
    public byte MagicAttack { get; set; }    // 0=none, 1=enabled, 2=single, 4-5=area
    public int Magic1 { get; set; }          // Primary attack magic ID
    public int Magic2 { get; set; }          // Secondary magic ID (unused in original)
    public int Magic3 { get; set; }          // Healing magic ID

    public bool HasMagicAttack => MagicAttack > 0 && Magic1 > 0;
    public bool IsHealer => NpcType == NpcData.TypeHealer && Magic3 > 0;

    private const byte ActTypeRetaliateFirst = 1;
    private const byte ActTypeRetaliateLast = 4;
    private const byte ActTypeCallsAlliesFirst = 3;
    private const byte ActTypeCallsAlliesLast = 4;
    private const byte DirectAttackMelee = 0;
    public const float MeleeAttackRange = 3f;
    public const float ChaseRangeSlack = 5f;
    public const float LostTargetDistance = 75f;

    public bool IsAggressive { get; set; }
    public float BulkRadius => Bulk / 100f * (Size > 0 ? Size / 100f : 1f);
    public float AttackDistance => DirectAttack == DirectAttackMelee ? MeleeAttackRange + BulkRadius : AttackRange;
    public float ChaseRange(bool targetDamagedMe) => targetDamagedMe ? TracingRange : SearchRange + ChaseRangeSlack;

    // AI runtime state
    public NpcState State { get; set; } = NpcState.Standing;
    public int TargetUserId { get; set; }   // CharacterId of aggro target
    public long LastAttackTicks { get; set; }
    public long WakeTicks { get; set; }
    public long StateChangeTicks { get; set; }
    public long LastRegenTicks { get; set; }

    public bool IsTracing { get; set; }
    public float TracingStartX { get; set; }
    public float TracingStartZ { get; set; }

    // Magic casting state
    public int ActiveSkillId { get; set; }
    public int ActiveTargetId { get; set; }  // CharacterId (player) or UniqueId (NPC) depending on context
    public bool HealTargetIsNpc { get; set; } // true when ActiveTargetId refers to an NPC UniqueId
    public long CastEndTicks { get; set; }

    // Damage tracking for EXP/drop distribution
    public Dictionary<int, int> DamageMap { get; } = []; // CharacterId → totalDamage
    public int TopDamagerCharId { get; set; }
    public ConcurrentDictionary<int, ActiveOverTimeEffect> ActiveOverTimeEffects { get; } = new();

    // Group behavior
    public byte Family { get; set; } // NPC family type for group calling
    public bool HasFriends => ActType is >= ActTypeCallsAlliesFirst and <= ActTypeCallsAlliesLast;

    public static bool AttacksFirst(byte actType)
        => actType is < ActTypeRetaliateFirst or > ActTypeRetaliateLast;

    public bool WasDamagedBy(int charId) => WithLock(_ => DamageMap.ContainsKey(charId));

    public void ForgetDamagers() => WithLock(_ =>
    {
        DamageMap.Clear();
        TopDamagerCharId = 0;
    });

    public void RecordDamage(int charId, int damage, UserSession? attacker = null, Func<int, UserSession?>? getSession = null)
    {
        WithLock(_ =>
        {
            if (DamageMap.TryGetValue(charId, out var existing))
                DamageMap[charId] = existing + damage;
            else
                DamageMap[charId] = damage;

            if (TopDamagerCharId == 0 || DamageMap[charId] > (DamageMap.TryGetValue(TopDamagerCharId, out var topDmg) ? topDmg : 0))
                TopDamagerCharId = charId;
        });

        // Scarecrows (training dummies) never aggro or chase attackers
        if (IsScarecrow)
            return;

        // Reactive aggro: if passive mob has no target, aggro the attacker
        if (TargetUserId == 0 && attacker != null)
        {
            EngageTarget(attacker, allowStateInterrupt: true);
            return;
        }

        // Evaluate target switch using 3 random criteria
        if (attacker != null && getSession != null && TargetUserId != charId)
            EvaluateTargetSwitch(attacker, getSession);
    }

    private void EvaluateTargetSwitch(UserSession newTarget, Func<int, UserSession?> getSession)
    {
        var currentTarget = getSession(TargetUserId);
        if (currentTarget == null || currentTarget.Hp <= 0)
        {
            // Current target gone — switch immediately
            TargetUserId = newTarget.CharacterId;
            return;
        }

        if (newTarget.CharacterId == currentTarget.CharacterId)
            return;

        int roll = Random.Shared.Next(0, 100);

        if (roll < 50)
        {
            // Criteria 1: Compare damage each player deals to NPC
            // Higher damage = more threatening = keep targeting them
            var (dmgFromCurrent, dmgFromNew) = WithLock(_ =>
            {
                DamageMap.TryGetValue(currentTarget.CharacterId, out int a);
                DamageMap.TryGetValue(newTarget.CharacterId, out int b);
                return (a, b);
            });
            if (dmgFromCurrent > dmgFromNew)
                return; // Current target is more threatening, keep it
        }
        else if (roll < 80)
        {
            // Criteria 2: Compare distance (closer target preferred)
            float distCurrent = DistSqTo(currentTarget.X, currentTarget.Z);
            float distNew = DistSqTo(newTarget.X, newTarget.Z);
            if (distNew > distCurrent)
                return; // New target is farther, keep current
        }
        else if (roll < 95)
        {
            // Criteria 3: Compare damage NPC deals to each player
            // Higher damage = easier kill = keep targeting them
            int dmgToCurrent = EstimateDamageToPlayer(currentTarget);
            int dmgToNew = EstimateDamageToPlayer(newTarget);
            if (dmgToCurrent > dmgToNew)
                return; // Current target takes more damage (easier kill), keep it
        }
        // 95-100%: always switch (implicit fall-through)

        TargetUserId = newTarget.CharacterId;
        StateChangeTicks = DateTime.UtcNow.Ticks;

        UpdateCombatStateForTarget(newTarget, allowStateInterrupt: State is NpcState.Standing or NpcState.Moving or NpcState.Returning);
    }

    private int EstimateDamageToPlayer(UserSession target)
    {
        int totalHit = Attack1 > 0 ? Attack1 : Attack2;
        int ac = target.Stats.TotalAc < 0 ? 0 : target.Stats.TotalAc;
        return totalHit * 200 / (ac + 240);
    }

    public void BeginTracing()
    {
        if (IsTracing)
            return;

        IsTracing = true;
        TracingStartX = X;
        TracingStartZ = Z;
    }

    public void EngageTarget(UserSession target, bool allowStateInterrupt)
    {
        TargetUserId = target.CharacterId;
        StateChangeTicks = DateTime.UtcNow.Ticks;
        UpdateCombatStateForTarget(target, allowStateInterrupt);
    }

    private void UpdateCombatStateForTarget(UserSession target, bool allowStateInterrupt)
    {
        if (!allowStateInterrupt || State == NpcState.Sleeping)
            return;

        float distSq = DistSqTo(target.X, target.Z);
        State = distSq <= AttackDistance * AttackDistance ? NpcState.Fighting : NpcState.Attacking;
        if (State == NpcState.Fighting)
            IsMoving = false;
    }

    private float DistSqTo(float x, float z)
    {
        float dx = X - x;
        float dz = Z - z;
        return dx * dx + dz * dz;
    }

    // Respawn
    public long DeathTimeTicks { get; set; }
    public bool IsDead => Hp <= 0 && DeathTimeTicks > 0;
    public const int DefaultRespawnDelayMs = 30000;

    public int RespawnDelayMs { get; set; } = DefaultRespawnDelayMs;
    public NpcRespawnType RespawnType { get; set; } = NpcRespawnType.Normal;
    public bool CanRespawn => RespawnType != NpcRespawnType.Never;

    public byte SpawnActType { get; set; }
    public short TrapNumber { get; set; }
    public bool UsesNpcSpawnStyle => SpawnActType >= 100;
    public bool IsAlive => Hp > 0;
    public bool IsNpc => !IsMonster;
    public bool IsBoss => NpcType == NpcData.TypeBoss;
    public bool IsScarecrow => NpcType == NpcData.TypeScarecrow;
    public bool IsAttackable => IsMonster || IsScarecrow; // Anything players can hit
    public bool IsGuard => NpcType is >= NpcData.TypeGuard and <= NpcData.TypeWarGuard;
    public bool IsNationOwned => Nation is EntityNation.Karus or EntityNation.ElMorad;
    public bool HasAi => IsMonster || IsGuard || IsScarecrow || IsNationOwned || FollowsAPath(MoveType);
    public EntityNation Nation { get; set; }
    public bool GateOpen { get; set; }
    public bool IsGate => NpcType == NpcData.TypeGate;
    public const byte MapObjectType = 1;
    public byte ObjectType { get; set; }

    public short GetPosX => (short)(X * 10);
    public short GetPosZ => (short)(Z * 10);
    public short GetPosY => (short)(Y * 10);

    public int RegionX { get; set; } = -1;
    public int RegionZ { get; set; } = -1;
    public int NewRegionX => (int)(X / RegionManager.RegionSize);
    public int NewRegionZ => (int)(Z / RegionManager.RegionSize);

    public void Respawn()
    {
        Hp = MaxHp;
        Mp = MaxMp;
        X = SpawnX;
        Y = SpawnY;
        Z = SpawnZ;
        DeathTimeTicks = 0;
        IsMoving = false;
        State = NpcState.Standing;
        TargetUserId = 0;
        LastAttackTicks = 0;
        WakeTicks = 0;
        StateChangeTicks = 0;
        ActiveSkillId = 0;
        ActiveTargetId = 0;
        HealTargetIsNpc = false;
        CastEndTicks = 0;
        IsTracing = false;
        TracingStartX = 0;
        TracingStartZ = 0;
        DamageMap.Clear();
        TopDamagerCharId = 0;
        ActiveOverTimeEffects.Clear();
        MoveType = InitMoveType;
        CurrentWaypoint = 0;
    }

    private const int SpawnPrecision = 10;
    private static NpcRespawnType ToRespawnType(short regenType) => regenType switch
    {
        (short)NpcRespawnType.OnceOnly => NpcRespawnType.OnceOnly,
        (short)NpcRespawnType.Never => NpcRespawnType.Never,
        _ => NpcRespawnType.Normal,
    };

    private const int MillisecondsPerSecond = 1000;
    public static bool FollowsAPath(NpcMoveType moveType) =>
        moveType is NpcMoveType.PatrolLoop or NpcMoveType.PatrolOnce or NpcMoveType.ScriptedPath;
    private static readonly Random _spawnRng = new();

    public static (float X, float Z) RandomSpawnPoint(NpcPosData pos)
    {
        if (pos.SpawnRange <= 0)
            return (pos.LeftX, pos.TopZ);

        var span = pos.SpawnRange * SpawnPrecision;
        var x = (pos.LeftX * SpawnPrecision + _spawnRng.Next(-span, span + 1)) / (float)SpawnPrecision;
        var z = (pos.TopZ * SpawnPrecision + _spawnRng.Next(-span, span + 1)) / (float)SpawnPrecision;
        return (x, z);
    }

    public static NpcInstance FromData(NpcData npc, NpcPosData pos, int uniqueId)
    {
        var (spawnX, spawnZ) = RandomSpawnPoint(pos);

        // MoveType: monsters use ActType directly, NPCs subtract 100
        byte rawMoveType = pos.ActType;
        if (rawMoveType >= NpcPosData.NpcSpawnActTypeBase)
            rawMoveType = (byte)(rawMoveType - NpcPosData.NpcSpawnActTypeBase);

        var moveType = Enum.IsDefined(typeof(NpcMoveType), rawMoveType)
            ? (NpcMoveType)rawMoveType
            : NpcMoveType.None;

        var waypoints = ParseWaypoints(pos.DotCnt, pos.Path);

        if (FollowsAPath(moveType) && waypoints.Length == 0)
            moveType = NpcMoveType.Wander;

        var instance = new NpcInstance
        {
            UniqueId = uniqueId,
            NpcId = npc.Id,
            Name = npc.Name,
            NpcType = npc.NpcType,
            SpawnActType = pos.ActType,
            TrapNumber = pos.TrapNumber,
            Group = npc.Group,
            Level = npc.Level,
            MaxHp = npc.Hp,
            Hp = npc.Hp,
            MaxMp = npc.Mp,
            Mp = npc.Mp,
            Ac = npc.Ac,
            Attack1 = npc.Attack1,
            Attack2 = npc.Attack2,
            SellingGroup = npc.SellingGroup,
            ModelId = npc.ModelId,
            Size = npc.Size > 0 ? npc.Size : (short)100,
            WeaponType1 = npc.WeaponType1,
            WeaponType2 = npc.WeaponType2,
            GoldDrop = npc.Money,
            Experience = npc.Experience,
            DropItemGroup = npc.ItemGroup,
            IsMonster = npc.IsMonster,
            ActType = npc.ActType,
            IsAggressive = AttacksFirst(npc.ActType),
            AttackDelay = npc.AttackDelay > 0 ? npc.AttackDelay : (short)2000,
            NpcSpeed = npc.Speed > 0 ? npc.Speed : (short)1000,
            Speed1 = npc.Speed1,
            Speed2 = npc.Speed2,
            AttackRange = npc.AttackRange > 0 ? npc.AttackRange : (byte)3,
            SearchRange = npc.SearchRange > 0 ? npc.SearchRange : (byte)8,
            TracingRange = npc.TracingRange > 0 ? npc.TracingRange : (byte)20,
            Bulk = npc.Bulk,
            HitRate = npc.HitRate,
            EvadeRate = npc.EvadeRate,
            FireR = npc.FireR,
            ColdR = npc.ColdR,
            LightningR = npc.LightningR,
            MagicR = npc.MagicR,
            PoisonR = npc.PoisonR,
            CurseR = npc.CurseR,
            DirectAttack = npc.DirectAttack,
            MagicAttack = npc.MagicAttack,
            Magic1 = npc.Magic1,
            Magic2 = npc.Magic2,
            Magic3 = npc.Magic3,
            Family = npc.Family,
            X = spawnX,
            Y = 0,
            Z = spawnZ,
            SpawnX = spawnX,
            SpawnY = 0,
            SpawnZ = spawnZ,
            ZoneId = (byte)pos.ZoneId,
            Direction = (short)pos.Direction,
            MoveType = moveType,
            InitMoveType = moveType,
            Waypoints = waypoints,
            RespawnDelayMs = pos.RegTime > 0 ? pos.RegTime * MillisecondsPerSecond : DefaultRespawnDelayMs,
            RespawnType = ToRespawnType(pos.RegenType),
            Nation = (EntityNation)npc.Group
        };
        if (instance.IsMonster && instance.Nation == EntityNation.All)
            instance.Nation = EntityNation.None;

        return instance;
    }

    public static NpcInstance FromObjectEvent(NpcData npc, ObjectEvent evt, short zoneId, int uniqueId)
    {
        var instance = new NpcInstance
        {
            UniqueId = uniqueId,
            NpcId = npc.Id,
            Name = npc.Name,
            NpcType = npc.NpcType,
            SpawnActType = 100,
            Group = npc.Group,
            Level = npc.Level,
            MaxHp = npc.Hp,
            Hp = npc.Hp,
            MaxMp = npc.Mp,
            Mp = npc.Mp,
            Ac = npc.Ac,
            Attack1 = npc.Attack1,
            Attack2 = npc.Attack2,
            SellingGroup = npc.SellingGroup,
            ModelId = npc.IsMonster ? (short)0 : npc.ModelId,
            Size = npc.Size > 0 ? npc.Size : (short)100,
            WeaponType1 = npc.WeaponType1,
            WeaponType2 = npc.WeaponType2,
            GoldDrop = 0,
            Experience = 0,
            DropItemGroup = 0,
            IsMonster = false,
            ActType = npc.ActType,
            IsAggressive = AttacksFirst(npc.ActType),
            AttackDelay = npc.AttackDelay > 0 ? npc.AttackDelay : (short)2000,
            NpcSpeed = npc.Speed > 0 ? npc.Speed : (short)1000,
            Speed1 = npc.Speed1,
            Speed2 = npc.Speed2,
            AttackRange = npc.AttackRange > 0 ? npc.AttackRange : (byte)3,
            SearchRange = npc.SearchRange > 0 ? npc.SearchRange : (byte)8,
            TracingRange = npc.TracingRange > 0 ? npc.TracingRange : (byte)20,
            Bulk = npc.Bulk,
            HitRate = npc.HitRate,
            EvadeRate = npc.EvadeRate,
            FireR = npc.FireR,
            ColdR = npc.ColdR,
            LightningR = npc.LightningR,
            MagicR = npc.MagicR,
            PoisonR = npc.PoisonR,
            CurseR = npc.CurseR,
            DirectAttack = npc.DirectAttack,
            MagicAttack = npc.MagicAttack,
            Magic1 = npc.Magic1,
            Magic2 = npc.Magic2,
            Magic3 = npc.Magic3,
            Family = npc.Family,
            X = evt.PosX,
            Y = evt.PosY,
            Z = evt.PosZ,
            SpawnX = evt.PosX,
            SpawnY = evt.PosY,
            SpawnZ = evt.PosZ,
            ZoneId = (byte)zoneId,
            MoveType = NpcMoveType.Stationary,
            InitMoveType = NpcMoveType.Stationary,
            Waypoints = [],
            ObjectType = MapObjectType,
            GateOpen = evt.Status != 0,
            Nation = (EntityNation)npc.Group
        };

        if (instance.Nation == EntityNation.All)
            instance.Nation = evt.Belong is (byte)EntityNation.Karus or (byte)EntityNation.ElMorad
                ? (EntityNation)evt.Belong
                : EntityNation.None;

        return instance;
    }

    private static (short X, short Z)[] ParseWaypoints(byte dotCnt, string? path)
    {
        if (dotCnt == 0 || string.IsNullOrEmpty(path)) return [];
        if (path.Length < dotCnt * 8) return [];

        var result = new (short X, short Z)[dotCnt];
        for (int i = 0; i < dotCnt; i++)
        {
            int offset = i * 8;
            if (short.TryParse(path.AsSpan(offset, 4), out var x)
                && short.TryParse(path.AsSpan(offset + 4, 4), out var z))
            {
                result[i] = (x, z);
            }
        }
        return result;
    }
}
