# FFXVI Reverse Engineering Documentation

## Table of Contents
- [Functions](#functions)
- [Structures](#structures)
- [Global Pointers](#global-pointers)
- [Magic IDs](#magic-ids)
- [Eikon IDs](#eikon-ids)
- [Action IDs](#action-ids)

---

## Functions

### FireContextualMagicProjectile
**Offset:** `0x56E0F0`  
**Signature:** *(hardcoded offset, signature matched wrong function)*

```c
long FireContextualMagicProjectile(MagicManager* magicManager, void* projectileData)
```

**Description:** Fires magic projectiles (Normal Shot, Charged Shot). Only called for magic shots, not melee or Eikon abilities.

**Parameters:**
- `RCX` (magicManager): Pointer to MagicManager structure
- `RDX` (projectileData): Pointer to projectile data (**NOTE: This parameter is IGNORED by the function! All data comes from magicManager**)

**Return:** Non-zero on success

**Notes:**
- The shot type is determined by `[magicManager + 0x38] + 0x10`
- To change shot type, modify `*(int*)([magicManager + 0x38] + 0x10)`

---

### MagicExecute
**Signature:** `48 8B C4 48 89 58 08 48 89 70 10 57 48 83 EC 60 8B FA 66 C7 40 E8 01 00 48 8B F1 C6 40 EA 00 C5 F9 EF C0 49 8B D1 48 8D 48 D8 C5 FA 7F 40 D8 49 8B D8`

```c
long MagicExecute(UnkMagicStruct* magicStruct, int magicId, void* a3, void* a4, int a5, int a6, int a7)
```

**Description:** Prepares the magic spell to be cast, sets up the magic struct.

**Parameters:**
- `RCX` (magicStruct): Pointer to UnkMagicStruct
- `RDX` (magicId): Magic spell ID (e.g., 214 for Normal Shot)
- `R8` (a3): Unknown pointer
- `R9` (a4): Unknown pointer
- Stack (a5): Unknown int (observed: 101)
- Stack (a6): Action ID (observed: 218 for Normal Shot)
- Stack (a7): Unknown int (observed: 694431488)

**Notes:**
- Must be called before CastMagic
- a6 appears to be the ActionId used in OnHit

---

### CastMagic
**Signature:** `48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8B 41 10 48 8B F2 48 8B 0D`

```c
char CastMagic(void* a1, UnkMagicStruct* magicStruct)
```

**Description:** Actually spawns the magic spell using the already set-up magic struct.

**Parameters:**
- `RCX` (a1): Unknown context pointer (must be cached from previous call)
- `RDX` (magicStruct): Pointer to the prepared UnkMagicStruct

**Notes:**
- Must be called after MagicExecute
- Both functions must have executed at least once after level load before manual spawning works

---

### GetTimeline
**Offset:** `0x4692A4`

```c
Timeline* GetTimeline(void* param_1)
```

**Description:** Retrieves the Timeline/Animation object.

**Parameters:**
- `RCX` (param_1): Context pointer, `[param_1 + 0x10]` contains the timeline pointer

**Return:** Timeline pointer or NULL

---

### OnHit
**Signature:** `48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 44 8B 82`

```c
// Ghidra shows 2 params, but hooks capture 4 (extra registers)
void OnHit(longlong* param_1, longlong param_2)
// Hook signature:
long OnHit(BnpcRow* bnpcRow, HitInfo* hitInfo, long a3, long a4)
```

**Description:** Called when an attack hits a target.

**Parameters:**
- `RCX` (param_1/bnpcRow): Pointer to target entity (BnpcRow)
- `RDX` (param_2/hitInfo): Pointer to HitInfo structure with attack details

**Key Offsets in param_1 (BnpcRow/Target):**
- `+0x20` → `+0x7298` → `+0x38`: Attack target ID

**Key Offsets in param_2 (HitInfo/R15):**
| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0x88 | int | EntityId? | Used in entity lookup |
| 0x8C | int | HitType? | Compared with 1 |
| **0xB0** | **int** | **ActionId** | **ID of the action/spell** |
| 0xD8 | ptr | ??? | Pointer to another struct |
| 0x168 | int | ??? | Unknown |
| **0x174** | **int** | **RawDamage** | **Damage amount** |
| 0x17C | int | ??? | Unknown ID |
| 0x194 | uint | Flags | Bitflags (bits 1,14,15,27,29 used) |

**Notes:**
- Ghidra decompiles with 2 parameters, but x64 calling convention passes extra values in R8/R9
- Reloaded-II hooks can capture these extra registers as a3, a4

---

### OnPerfectDodge
**Signature:** `48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 57 41 54 41 55 41 56 41 57 48 83 EC ?? 45 33 ED 4C 8B FA`

```c
char OnPerfectDodge(void* a1, void* a2, double a3, double a4)
```

**Description:** Called when player performs a perfect dodge.

---

### IsSummonModeActive
**Signature:** `48 89 5C 24 ?? 57 48 83 EC ?? 8B FA 85 D2`

```c
byte IsSummonModeActive(void* playerState, int summonModeId)
```

**Description:** Checks if a specific Eikon/summon mode is active.

**Parameters:**
- `RCX` (playerState): Player state pointer (from global at `0x1816608`)
- `RDX` (summonModeId): Eikon ID to check

**Return:** 1 if active, 0 if not

---

### StartPlayerMode
**Signature:** `85 D2 0F 84 ?? ?? ?? ?? 48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 48 89 78 ?? 41 55 41 56 41 57 48 83 EC ?? 48 8D 79`

```c
char StartPlayerMode(void* a1, uint playerMode, long a3)
```

**Description:** Called when player mode changes (Eikon switches, etc.)

---

### CopyAttackData
**Signature:** `48 89 5C 24 ?? 57 48 83 EC 20 48 8D 59 58 48 8B FA`

```c
void CopyAttackData(AttackStruct* dest, AttackTemplate* src)
```

**Description:** Copies attack data from template to attack struct when creating projectiles/attacks.

**Key Offset:**
- `+0x58`: ActionId in both src and dest

---

### BattleTechnique
**Signature:** `48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 45 8A F8`

```c
char BattleTechnique(void* a1, uint techId, char a3)
```

**Description:** Called for special abilities (not normal attacks).

---

## Structures

### MagicManager
**Size:** ~0x200 bytes (512)

```c
struct MagicManager {
    // +0x00: Unknown
    // +0x28: Timeline pointer
    void* timeline;          // +0x28
    // +0x30: Unknown
    MagicInputConfig* config; // +0x38 - Pointer to input configuration
    // ...
};
```

---

### MagicInputConfig
**Size:** ~0x100 bytes (256)

```c
struct MagicInputConfig {
    // +0x00: Unknown
    // +0x10: Shot Type
    int shotType;      // +0x10 (1=Normal, 2=Charged, 3=Precision Counter)
    int unknown18;     // +0x18
    int unknown1C;     // +0x1C
    // ...
};
```

**Shot Types:**
- `1` = Normal Shot (Dia)
- `2` = Charged Shot
- `3` = Precision Counter
- `4` = Magic Burst

---

### UnkMagicStruct
**Size:** 0x108 bytes (264)

```c
struct UnkMagicStruct {
    long ptr1;              // +0x00 - First pointer (dereference on copy)
    long unkLongArray[32];  // +0x08 - Array of 32 longs
};
```

**Notes:**
- When copying this struct, `ptr1` should be read as `*(long*)original_ptr1`
- The array starts at offset 0x08

---

### Timeline
**Size:** Unknown

```c
struct Timeline {
    void* vtable;           // +0x00 - VTable pointer
    void* ptr1;             // +0x08
    void* ptr2;             // +0x10
    void* ptr3;             // +0x18
    // +0x20: Contains flags
    // +0x88: Points to another structure
    // ...
};
```

**VTable Functions of Interest:**
- `vtable[0x48]`: IsMagicBlocked? - Returns whether magic is allowed
- `vtable[0x58]`: GetMagicType?
- `vtable[0x98]`: IsMagicAllowed?

---

### AttackInfo (R15 in OnHit)

```c
struct AttackInfo {
    // ...
    long entityIdPtr;    // +0x88 (136)
    // ...
    int actionId;        // +0xB0 (176)
    // ...
    int rawDamage;       // +0x174 (372)
    // ...
};
```

---

## Global Pointers

| Name | Offset | Description |
|------|--------|-------------|
| GlobalEntityManager | `0x1816CD0` | Entity manager pointer |
| GlobalPlayerState | `0x1816608` | Player state (for IsSummonModeActive) |

**Usage:**
```c
long entityManager = *(long*)(baseAddress + 0x1816CD0);
long playerState = *(long*)(baseAddress + 0x1816608);
```

---

## Magic IDs

| ID | Name | Description |
|----|------|-------------|
| 214 | Normal Shot Magic | Used in MagicExecute for Dia |
| 218 | Normal Shot Action | ActionId for Normal Shot hit |
| 219 | Charged Shot Action | ActionId for Charged Shot hit |
| 227 | Unknown Magic | Another magic-related ActionId |

---

## Eikon IDs

| ID | Eikon |
|----|-------|
| 1 | Phoenix |
| 2 | Garuda |
| 3 | Ramuh |
| 4 | Titan |
| 5 | ? |
| 6 | Shiva |
| 7 | ? |
| 8 | **Bahamut** |
| 9 | Odin |
| 10 | Leviathan |

---

## Action IDs (Partial List)

| ID | Description |
|----|-------------|
| 218 | Normal Shot (Dia) |
| 219 | Charged Shot |
| 227 | Unknown Magic |
| 10000-20000 | Ifrit moves (spammy) |

---

## Spawning Magic On Demand

### Process:
1. **Cache context** when MagicExecute and CastMagic are called naturally
2. **Store:**
   - UnkMagicStruct (deep copy, 264 bytes)
   - a3, a4, a5, a6, a7 from MagicExecute
   - a1 from CastMagic
3. **To spawn:**
   ```c
   MagicExecute(cachedStruct, magicId, cached_a3, cached_a4, cached_a5, cached_a6, cached_a7);
   CastMagic(cached_a1, cachedStruct);
   ```

### Warning:
- Context becomes invalid after level load
- Must fire at least one normal shot after loading to capture valid context
- Attempting to spawn before context is ready will crash the game

---

## Version History

| Date | Notes |
|------|-------|
| 2024-12-10 | Initial documentation |
| 2024-12-10 | Added MagicExecute/CastMagic from Discord community |
| 2024-12-10 | Verified magic spawning works during Perfect Dodge |

---

## Contributors
- Original RE work from FFXVI modding Discord community
- Additional analysis during Diara system development
