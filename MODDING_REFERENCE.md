# FFXVI Modding Reference Manual
## Truly Eikonic Spells Mod - Technical Documentation

> **Last Updated:** December 7, 2025  
> **Status Legend:**  
> ✅ Confirmed - Tested and working  
> 🔬 Testing - Under investigation  
> ❓ Unconfirmed - Theoretical/needs verification  
> ❌ Failed - Tested and doesn't work as expected

---

## Table of Contents
1. [Game Architecture Overview](#game-architecture-overview)
2. [Memory Addresses & Pointers](#memory-addresses--pointers)
3. [Function Signatures](#function-signatures)
4. [Action IDs](#action-ids)
5. [Eikon IDs](#eikon-ids)
6. [NEX Tables](#nex-tables)
7. [Data Structures](#data-structures)
8. [Hooks Implementation](#hooks-implementation)
9. [Timelines & Animations](#timelines--animations)
10. [Research Notes](#research-notes)

---

## Game Architecture Overview

### Mod Framework Stack
```
┌─────────────────────────────────────┐
│         Reloaded II Loader          │
├─────────────────────────────────────┤
│      FF16Framework.Interfaces       │
│  (NEX API, Hooks, Memory Access)    │
├─────────────────────────────────────┤
│     Reloaded.Hooks.ReloadedII       │
│  (Function Hooking & Detouring)     │
├─────────────────────────────────────┤
│   Reloaded.Memory.SigScan.ReloadedII│
│     (Signature Pattern Scanning)    │
├─────────────────────────────────────┤
│           FFXVI Game                │
└─────────────────────────────────────┘
```

### Key Concepts
- **NEX Tables**: Game data tables (like Excel spreadsheets) containing stats, IDs, parameters
- **Hooks**: Intercept game functions to modify behavior
- **Wrappers**: Call game functions directly without interception
- **Signatures**: Byte patterns to find functions in memory

---

## Memory Addresses & Pointers

### Global Pointers (Relative to Base Address)

| Pointer Name | Offset | Description | Status |
|:-------------|:-------|:------------|:-------|
| `globalEntityManagerPtr` | `+0x1816CD0` | Entity manager for getting/creating entities | ✅ Confirmed |
| `globalPlayerStatePtr` | `+0x1816608` | Player state base pointer | ✅ Confirmed |

### IsSummonModeActive Memory Formula ✅ Confirmed
```csharp
// Formula to check which Eikon is currently active
long basePtr = *(long*)(baseAddress + 0x1816608);
long isSummonModeActive_a1 = basePtr + 0x4798 + 0x14650;

// Read comparison value
long offset70 = *(long*)(isSummonModeActive_a1 + 0x70);
long compareValue = *(long*)(isSummonModeActive_a1 + 0x58 + offset70 * 8);

// Check each Eikon: [isSummonModeActive_a1 + 8 * eikonId] == compareValue
long eikonValue = *(long*)(isSummonModeActive_a1 + 8 * eikonId);
bool isActive = (eikonValue == compareValue);
```

---

## Function Signatures

### Confirmed Working ✅

#### OnHit - Damage Application
```
Signature: 48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 44 8B 82

Delegate: unsafe long OnHitDelegate(long* bnpcRow, long R15, long a3, long a4)

Parameters:
- bnpcRow: Pointer to target entity (enemy)
- R15: Attack info structure (contains ActionId, damage, etc.)
- a3, a4: Unknown additional parameters

Usage: Hook to intercept all damage events, modify damage, track targets
```

#### OnPerfectDodge - Perfect Dodge Detection
```
Signature: 48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 57 41 54 41 55 41 56 41 57 48 83 EC ?? 45 33 ED 4C 8B FA

Delegate: unsafe char OnPerfectDodgeDelegate(long a1, long a2, double a3, double a4)

Parameters:
- a1: Unknown (possibly player state)
- a2: Unknown (possibly attacker info)
- a3, a4: Unknown doubles

Usage: Detect when player performs a perfect dodge
Note: Called BEFORE MaybeHandleWingsPerfectDodge
```

#### OnLevelLoad - Level/Area Loading
```
Signature: 48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 55 41 54 41 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 8B 41

Delegate: long OnLevelLoad(long a1, double a2, double a3, double a4)

Usage: Reset mod state when loading new areas
```

#### StartPlayerMode - Player Mode Changes
```
Signature: 85 D2 0F 84 ?? ?? ?? ?? 48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 48 89 78 ?? 41 55 41 56 41 57 48 83 EC ?? 48 8D 79

Delegate: unsafe char StartPlayerModeDelegate(long a1, uint playerMode, long a3)

Parameters:
- a1: Player mode structure pointer
- playerMode: Mode ID being activated
- a3: Unknown

Usage: Track Eikon mode changes
Note: playerMode values don't directly map to Eikon IDs - use IsSummonModeActive instead
```

#### GetOrCreateEntity - Entity Management
```
Signature: 48 89 5C 24 ?? 48 89 6C 24 ?? 44 89 44 24 ?? 56 57 41 54 41 56 41 57 48 83 EC ?? 45 33 E4

Delegate: long GetOrCreateEntityDelegate(long entityManager, out long outEntityInfo, long entityIdPtr)

Usage: Get entity information from entity manager
```

#### GetBnpcIdFromEntity - Get Entity ID
```
Signature: 48 83 EC ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? 8B 40

Delegate: long GetBnpcIdFromEntityDelegate(long entity)

Usage: Extract BNPC ID from entity pointer (used to identify Clive vs enemies)
```

#### OnBattleTechnique - Special Ability Execution
```
Signature: 48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 45 8A F8

Delegate: char OnBattleTechniqueDelegate(long a1, uint techId, char a3)

Parameters:
- a1: Context pointer (store for potential manual invocation)
- techId: Technique/ability ID
- a3: Unknown char

Usage: Log special abilities, potentially trigger abilities
Note: Only called for SPECIAL abilities (named skills), NOT for normal attacks/magic
```

### Under Investigation 🔬

#### MaybeHandleWingsPerfectDodge - Wings of Light Effects
```
Signature: 48 89 5C 24 ?? 48 89 74 24 ?? 55 57 41 55 41 56 41 57 48 8B EC 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 48 8B DA

Delegate: unsafe long MaybeHandleWingsPerfectDodgeDelegate(long a1, long a2)

Contains checks for:
- Wings of Light dodge
- Leviathan dodge
- Escapement Bit dodge

Usage: Potentially trigger wings visual effects on dodge
Status: Hook added, needs testing for effect triggering
```

#### HandleEscapmentBitPerfectDodge - Escapement Bit Effects
```
Signature: 48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 48 89 78 ?? 41 54 41 56 41 57 48 83 EC ?? 48 8B EA C5 F8 29 70

Known behavior:
- Heals 30/60/100 HP based on dodged attack strength (calls ApplyDamage with positive value)
- Changes skill cooldown rate (200%, 250%, 300%)
- Activates buff by setting a bit to 1 and float to 1.35 (buff duration)

Status: Not yet hooked - potential reference for buff application
```

#### CopyAttackData - Attack Data Copy ✅ Hooked
```
Signature: 48 89 5C 24 ?? 57 48 83 EC 20 48 8D 59 58 48 8B FA
Offset: 0x597BED (relative to base)

Delegate: unsafe void CopyAttackDataDelegate(long destAttackStruct, long srcAttackTemplate)

Parameters:
- destAttackStruct: Destination attack structure (will be used as R15 in OnHit)
- srcAttackTemplate: Source template containing attack parameters

Behavior:
- Called when creating attacks/projectiles
- Copies data from template to attack structure
- Template contains ActionId at offset +0x58
- Same template address is reused for all magic types (219, 227, etc.)
- Same dest address is reused across attacks

Key finding: Template is modified BEFORE this function is called
```

#### PrepareAttackTemplate - Template Preparation 🔬 Under Investigation
```
Offset: 0x59FB6E (relative to base)
Address observed: 7FF77E9AFB6E

Assembly:
7FF77E9AFB62 - mov [rcx+000000A4],eax
7FF77E9AFB68 - mov eax,[rdx+000000A8]   ; Read from source (RDX - stack)
7FF77E9AFB6E - mov [rcx+000000A8],eax   ; Write to template (RCX)

Parameters:
- RCX: Template base (e.g., 0x1C967FB4100, which is template - 0x50)
- RDX: Source data from stack (contains actual attack parameters)

Behavior:
- Prepares the template before CopyAttackData is called
- Normal shot calls this once
- Charged shot calls this multiple times
- Source (RDX) is a dynamic stack address that changes constantly

Status: Need to trace further back to find attack initiation function
```

### Not Working / Failed ❌

#### IsSummonModeActive (Direct Function Call)
```
Signature: 48 89 5C 24 ?? 57 48 83 EC ?? 8B FA 85 D2

Result: Function was found but returned incorrect values (0 or garbage)
Solution: Use memory reading formula instead (see Memory Addresses section)
```

#### GetCurrentSummonMode
```
Result: Returned garbage values
Solution: Use IsSummonModeActive memory formula with iteration over known Eikon IDs
```

---

## Action IDs

> **Source**: [Pastebin - FFXVI Actions List](https://pastebin.com/Hk7tQLDM) + Our Testing  
> **Note**: ActionId in OnHit may differ from action.nxd IDs - needs verification

### Legend
- **E**: Value looked empty in action.nxd
- **U**: Upgraded version of Action/Ability
- **?**: Tested but does nothing noticeable in Free Roam
- **x**: Values do not exist in action.nxd

### Core Movement & Dodge

| ID | Name | Status |
|:---|:-----|:-------|
| 14 | Precision Dodge | ✅ From pastebin |
| 15 | Dodge | ✅ From pastebin |
| 23 | Berserker Dodge | ✅ From pastebin |
| 65 | Step-back | ✅ From pastebin |

### Magic Shot IDs ✅ Confirmed (Our Testing)

| ID | Name | Description |
|:---|:-----|:------------|
| 218 | Air Magic Shot | Basic magic when airborne |
| 219 | Ground Magic Shot | Basic magic on ground |
| 227 | Ground Charged Magic | Hold to charge, releases bigger projectile |
| 228 | Air Charged Magic | Charged shot in air |

### Magic Burst IDs ✅ Confirmed

| ID | Name | Description |
|:---|:-----|:------------|
| 199 | Magic Burst | First/part of melee magic combo |
| 200 | Magic Burst | Second part |
| 201 | Magic Burst | Third part |

### Sword Combos (from Pastebin)

| ID | Name | Status |
|:---|:-----|:-------|
| 139-145 | Combo 1 variations | ✅ From pastebin |
| 154 | Combo 2 | ✅ From pastebin |
| 155 | Combo 3 | ✅ From pastebin |
| 156 | Combo 4 | ✅ From pastebin |
| 157-159 | Aerial Combo 1-3 | ✅ From pastebin |
| 146-147 | Recovery Strike | ✅ From pastebin |

### Lunge & Downthrust

| ID | Name | Status |
|:---|:-----|:-------|
| 160-162 | Lunge (various) | ✅ From pastebin |
| 163-166 | Lunge Upgraded | ✅ From pastebin |
| 167-173 | Downthrust variations | ✅ From pastebin |

### Burning Blade

| ID | Name | Status |
|:---|:-----|:-------|
| 174 | Ground Burning Blade | ✅ From pastebin |
| 175 | Air Burning Blade | ✅ From pastebin |

### Limit Break Actions

| ID | Name | Status |
|:---|:-----|:-------|
| 259 | Limit Break Start Animation | ✅ From pastebin |
| 289 | Air Limit Break Start Animation | ✅ From pastebin |
| 301-310 | LB Lunge/Downthrust/BB | ✅ From pastebin |
| 316-322 | LB Combo 1-7 | ✅ From pastebin |

### Phoenix Abilities

| ID | Name | Status |
|:---|:-----|:-------|
| 334-336 | Wykes | ✅ From pastebin |
| 338-349 | Ignition (various) | ✅ From pastebin |
| 353-357 | Phoenix Shift | ✅ From pastebin |
| 358-361 | Perfect Dodge Attack variations | ✅ From pastebin |
| 363-364 | Rising Flames | ✅ From pastebin |
| 367-370 | Scarlet Cyclone | ✅ From pastebin |
| 375-381 | Heatwave | ✅ From pastebin |
| 385-386 | Rebirth | ✅ From pastebin |

### Garuda Abilities

| ID | Name | Status |
|:---|:-----|:-------|
| 401-406 | Deadly Embrace | ✅ From pastebin |
| 419-428 | Gouge | ✅ From pastebin |
| 439-444 | Wicked Wheel | ✅ From pastebin |
| 446-465 | Rook's Gambit | ✅ From pastebin |
| 478-479 | Aerial Blast | ✅ From pastebin |

### Titan Abilities

| ID | Name | Status |
|:---|:-----|:-------|
| 520-533 | Windup variations | ✅ From pastebin |

### Shiva Abilities

| ID | Name | Status |
|:---|:-----|:-------|
| 703-704 | Cold Snap | ✅ From pastebin |
| 714-715 | Frostbite | ✅ From pastebin |
| 718-719 | Permafrost | ✅ From pastebin |

### Odin Abilities

| ID | Name | Status |
|:---|:-----|:-------|
| 871-890 | Dark Aerial/Lunge/Downthrust | ✅ From pastebin |
| 898-902 | Zantetsuken Lvl 1-3 | ✅ From pastebin |

### Precision Shot ✅ Confirmed (Our Testing)

| ID | Name | Description |
|:---|:-----|:------------|
| 222 | Precision Shot | Timed shot after dodge (Bahamut) |

### Bahamut Abilities 🔬 Testing

| ID | Name | Status |
|:---|:-----|:-------|
| 776 | Megaflare (part 1) | 🔬 Testing |
| 777 | Megaflare (part 2) | 🔬 Testing |
| 800 | Megaflare (part 3) | 🔬 Testing |
| 801 | Megaflare (part 4) | 🔬 Testing |
| 824 | Impulse | 🔬 Testing |
| 830 | Flare Breath | 🔬 Testing |
| 845 | Gigaflare | 🔬 Testing |

### Synergy Abilities (Testing from DiaSystem)

| ID | Name | Eikon |
|:---|:-----|:------|
| 747-748 | Mesmerize | Shiva |
| 628 | Blind Justice | Odin |
| 376 | Heatwave | Phoenix |
| 1028-1124 | Leviathan abilities | Leviathan |

### BattleTechnique IDs 🔬 Testing

| techId | Description | Status |
|:-------|:------------|:-------|
| 51 | Normal Perfect Dodge counter | 🔬 Observed after normal perfect dodge |
| 53 | Perfect Dodge Counter (variant) | 🔬 Observed after perfect dodge |
| 211 | Charged Shot technique | ✅ Confirmed - appears when firing charged shot |
| 5011 | Wings of Light attack/dash | ✅ Confirmed - appears during Wings of Light mode |
| 10000-20000 | Ifrit moves range | ✅ Confirmed range |

### MaybeHandleWingsPerfectDodge Research 🔬

**Findings:**
- Function can be called without crash
- Returns `0x0` when Wings of Light mode is not active
- Has internal checks that prevent effect activation outside of Wings mode
- Simply calling the function doesn't trigger the wings visual effect

**Conclusion:**
Wings effect is likely controlled by:
1. A flag/bit that enables Wings of Light mode
2. The function only processes effects IF that flag is already set
3. Need to find and set the flag directly to enable wings outside normal gameplay

---

## Eikon IDs

### Summon Mode IDs ✅ Confirmed

| ID | Eikon | Notes |
|:---|:------|:------|
| 0 | Phoenix | Also shared with Leviathan and Ultima (DLC) |
| 2 | Garuda | |
| 3 | Titan | |
| 4 | Ramuh | |
| 5 | Shiva | |
| 7 | Odin | |
| 8 | Bahamut | |

### Known Eikon Array for Iteration
```csharp
private static readonly int[] KnownEikonIds = { 0, 2, 3, 4, 5, 7, 8 };
```

### Spell Elements Mapping
```csharp
public enum SpellElement { None, Fire, Dia, Dark, Aero, Ice, Thunder, Earth, Water, Ruin }

// Eikon to Element mapping
0 (Phoenix)  => Fire
8 (Bahamut)  => Dia (Light)
7 (Odin)     => Dark
2 (Garuda)   => Aero
5 (Shiva)    => Ice
4 (Ramuh)    => Thunder
3 (Titan)    => Earth
// Leviathan => Water (shares ID 0 with Phoenix)
```

---

## NEX Tables

### Known Tables

| Table Name | Version | Description | Status |
|:-----------|:--------|:------------|:-------|
| `attackparam` | 1.0.3 | Attack parameters, damage values | ✅ Used |
| `uisound` | 1.0.3 | UI sound effects | ✅ Available |
| `battlescorebonuslevel` | - | Battle score multipliers | ✅ Available |
| `ui` | - | UI configuration | ✅ Available |

### Loading NEX Tables
```csharp
// Load table layout
var layout = TableMappingReader.ReadTableLayout("attackparam", new Version(1, 0, 3));

// Get NEX API
_managedNexApi = _modLoader.GetController<INextExcelDBApiManaged>();
_rawNexApi = _modLoader.GetController<INextExcelDBApi>();

// Wait for NEX to load
managedApi.OnNexLoaded += OnNexLoaded;

// Access table rows
INexRow row = table.GetRow(rowId);
int value = row.GetInt32((uint)layout.Columns["ColumnName"].Offset);
```

### attackparam Columns 🔬 Needs Documentation
```
TODO: Dump all columns from attackparam layout to document available fields
```

---

## Data Structures

### R15 Attack Info Structure ✅ Confirmed

| Offset | Type | Description |
|:-------|:-----|:------------|
| +0xB0 | int | ActionId |
| +0x174 | int | Damage value (can be modified) |
| +0x88 (136) | long | Pointer to entity ID info |

### bnpcRow Target Structure ✅ Confirmed

```csharp
// Getting attack target ID from bnpcRow
var a = *(long*)((byte*)bnpcRow + 0x20);
var b = *(long*)((byte*)a + 0x7298);
uint attackTarget = *(uint*)((byte*)b + 0x38);
```

### Clive Entity IDs ✅ Confirmed
```csharp
// All IDs that represent Clive/player
private readonly HashSet<uint> _cliveIds = new() { 1, 2, 3, 4, 6, 8, 9, 10 };

// ID 100 can be Clive's attacks when target != 1
```

---

## Hooks Implementation

### Hook vs Wrapper

**Hook** (`CreateHook`): Intercepts function calls, can modify behavior
```csharp
_onHit = _hooks.CreateHook<OnHitDelegate>(OnHitImpl, address).Activate();

// In implementation, call original:
return _onHit.OriginalFunction(args...);
```

**Wrapper** (`CreateWrapper`): Allows calling game functions directly
```csharp
_getOrCreateEntity = _hooks.CreateWrapper<GetOrCreateEntityDelegate>(address, out _);

// Can call directly:
_getOrCreateEntity(entityManager, out entityInfo, entityIdPtr);
```

### Signature Scanning
```csharp
scans.AddScan("SIGNATURE_PATTERN", address => {
    // address is the found location
    _hook = _hooks.CreateHook<DelegateType>(Implementation, address).Activate();
});
```

---

## Timelines & Animations

### Overview (From Discord) 🔬 Research Needed

Timelines control:
- Attack animations
- VFX timing
- Damage windows
- Sound effects

### Modifying Attack Timelines

To change attack behavior via timelines:
1. Find `CharaTimelineId` in action row
2. Edit nested timeline pac in `c1001` (Clive's chara folder)
3. Add entry in `charatimelineIds.idl`
4. Use FF16Tools CLI or framework code

### Key Files
- Clive's character folder: `c1001`
- Timeline pack: nested inside character pack
- Timeline IDs file: `charatimelineIds.idl`

### Runtime Editing
- Most values can be edited at runtime with LiveNexEditor or framework
- Some changes require area reload
- Clive's moveset is mostly editable in real-time

---

## Research Notes

### Spawning Projectiles/Magic ❓ Unknown

**Goal**: Spawn Dia spells on-demand (e.g., during perfect dodge)

**Current Status**: No known function to spawn projectiles

**Approaches to investigate**:
1. Find `CreateProjectile` or similar function via signature scanning
2. Hook the function that creates Dia projectiles during normal shooting
3. Timeline modification to add projectile spawns
4. Call BattleTechnique with correct techId (only works for special abilities)

**Discord Info**:
> "Projectiles having varying speed/distances should be possible through the magic file"
> "spawn magic on-demand with function hooking"

---

## Projectile Creation System - Deep Dive 🔬

### Attack Flow (Confirmed)

```
┌─────────────────────────────────────────────────────────────────┐
│  1. Unknown initiator function                                   │
│     ↓                                                           │
│  2. PrepareAttackTemplate (0x59FB6E)                            │
│     - Copies attack params from stack to template               │
│     - RCX = template base, RDX = source (stack)                 │
│     ↓                                                           │
│  3. CopyAttackData (0x597BED)                                   │
│     - Copies template to attack struct                          │
│     - dest = attack struct (becomes R15 in OnHit)               │
│     - src = template with ActionId                              │
│     ↓                                                           │
│  4. [Projectile flies through air]                              │
│     ↓                                                           │
│  5. OnHit                                                        │
│     - R15 = attack struct created in step 3                     │
│     - Damage is applied                                         │
└─────────────────────────────────────────────────────────────────┘
```

### Key Addresses (Session-dependent)

| Component | Example Address | Notes |
|:----------|:----------------|:------|
| Template | `0x1C967FB4150` | Reused for all magic, modified before each attack |
| Dest struct | `0x1D8A8B0D5E0` | Reused, becomes R15 in OnHit |
| Template base | Template - 0x50 | Used by PrepareAttackTemplate |

### Template Structure

| Offset | Value (Normal) | Value (Charged) | Description |
|:-------|:---------------|:----------------|:------------|
| +0x00 | pointer | pointer | Unknown ptr |
| +0x08-0x28 | 0 | 0 | Zeros |
| +0x30 | 0x10400042A | 0x10400042A | Entity ID? (Clive?) |
| +0x40 | 0x3C (60) | 0x3C (60) | Unknown |
| +0x50 | varies | varies | Unknown |
| +0x58 | 219 | 227 | **ActionId** |
| +0x5C | 0 | 0 | Unknown |
| +0x60 | 67109441 | 67109442 | Incrementing counter? |
| +0x64 | 101 | 102 | Projectile type? |

### Attack Struct (R15) After Copy

| Offset | Description |
|:-------|:------------|
| +0x00 | VTable? (0x7FF77FA1B490) |
| +0x08 | Target entity |
| +0x10 | Target entity (duplicate) |
| +0x18 | Unknown ptr (0x7FF77F990598) |
| +0x20 | Player entity? |
| +0x88 | Entity ID (same as template +0x30) |
| +0xB0 | **ActionId** |
| +0x174 | **Damage value** |

### What We Need to Find

To spawn projectiles manually, we need:

1. **Allocation function**: What creates the dest attack struct?
   - The dest struct `0x1D8A8B0D5E0` is reused
   - Need to find how it's allocated initially
   
2. **Projectile spawn function**: What actually creates the visual projectile?
   - CopyAttackData only prepares the data
   - Something else must spawn the actual entity
   
3. **Or alternatively**: Find the function that initiates the entire attack sequence
   - Could be triggered by player input
   - Would need to fake player input or call directly

### Next Investigation Steps

1. **Hook PrepareAttackTemplate** (0x59FB6E) to see its caller
2. **Search for projectile spawn** - look for functions that:
   - Take position (float x, y, z)
   - Take ActionId
   - Are called around the same time as CopyAttackData
3. **Investigate VTable** at R15+0x00 (`0x7FF77FA1B490`)
   - Offset: 0x160B490 from base
   - May contain method pointers for projectile behavior

---

### Triggering Wings VFX ❓ Unknown

**Goal**: Show Bahamut wings during Diara buff

**Current Status**: Hooked `MaybeHandleWingsPerfectDodge` but haven't found how to trigger effect

**Approaches**:
1. Research MaybeHandleWingsPerfectDodge internal checks
2. Find the bit/flag that enables wings display
3. Look for VFX spawning functions

### Suppressing Projectile Damage ❓ Unknown

**Goal**: Diara charged shot shouldn't deal damage (only activates buff)

**Approaches**:
1. Set damage to 0 in OnHit when Diara conditions met
2. Return early from OnHit (risky - might break game state)
3. Modify R15 damage value to 0

### Enemy Tracking for Debuffs ❓ Unknown

**Goal**: Track all enemies for effects like Darkra debuff

**Discord Info**:
> "There's probably a function to return every bNpc in a battle, but I don't know where it is"

---

## Mod Configuration

### ModConfig.json Structure
```json
{
  "ModId": "ff16.gameplay.truly_eikonic_spells",
  "ModName": "Truly Eikonic Spells",
  "ModAuthor": "SalvadorDalike",
  "ModVersion": "0.1.0",
  "ModDll": "ff16.gameplay.truly_eikonic_spells.dll",
  "ModDependencies": [
    "ff16.utility.framework",
    "ff16.utility.modloader",
    "Reloaded.Memory.SigScan.ReloadedII",
    "reloaded.sharedlib.hooks"
  ],
  "SupportedAppId": ["ffxvi.exe"]
}
```

### Output Path
```
E:\Documentos\Mods\FFXVI\Tools\Reloaded\Release\Mods\ff16.gameplay.truly_eikonic_spells\
```

---

## Version History

### v0.1.0 - Current Development
- ✅ Dia System: Stacking damage bonus (50 stacks = 50% bonus)
- ✅ Eikon detection via memory reading
- ✅ OnHit hook for damage tracking
- ✅ Perfect Dodge detection
- 🔬 Diara System: Buff activation on charged shot
- ❓ Projectile spawning on perfect dodge (needs research)
- ❓ Wings VFX during Diara buff (needs research)

---

## Timeline System (From Discord Research)

### Overview
- **Tool**: 010Editor with mThund3R's templates/scripts
- **Purpose**: Each action/ability has a "timeline" that controls animations, hitboxes, VFX spawns, projectile creation
- **File Format**: `.tmln` files inside pac archives

### ControlPermission Elements
Según la conversación de Discord, los timelines contienen elementos `ControlPermission` que determinan qué puede hacer el jugador durante la animación:
- Cuándo puede cancelar
- Cuándo puede esquivar
- Qué inputs están permitidos

### Key Insight from Discord
> "spawn magic on-demand with function hooking"

Esto sugiere que existe una función que el juego llama para spawnear proyectiles mágicos. Los timelines probablemente llaman esta función en ciertos frames de la animación.

### Workflow Típico (Timeline Editing)
1. Extraer el .pac que contiene la acción
2. Abrir el .tmln en 010Editor con el template
3. Editar elementos (timing, VFX, hitboxes, projectile spawn)
4. Re-empaquetar el .pac
5. Hacer que el mod cargue el .pac modificado

### Potential Functions to Hook
Si el timeline llama funciones para spawnear magia, deberíamos buscar:
- `SpawnProjectile` / `CreateProjectile`
- `FireMagic` / `CastSpell`
- `CreateEntity` / `SpawnEntity`
- Funciones que reciban Action ID + posición + dirección

---

## Analysis: Approaches to Spawn Dia Spells

### Approach A: Function Hooking (Recommended First)

**Objetivo**: Encontrar la función que el juego usa para crear proyectiles mágicos.

**Pasos**:
1. Usar CheatEngine para breakpointear cuando Clive dispara magia normal (Action 219)
2. Trazar hacia atrás para encontrar la función de creación de proyectil
3. Documentar la signature: `CreateProjectile(posX, posY, posZ, actionId, ???)`
4. Hookear esa función para poder llamarla nosotros

**Pros**:
- Control total sobre qué, cuándo, y dónde spawnear
- No requiere modificar archivos del juego
- Podríamos spawnear 5 Dias con diferentes posiciones/ángulos

**Contras**:
- Requiere ingeniería inversa significativa
- La función puede tener muchos parámetros desconocidos

### Approach B: Triggear una Acción Existente

**Objetivo**: Forzar al juego a ejecutar la animación/proyectil de una acción.

**Pasos**:
1. Encontrar función tipo `ExecuteAction(playerId, actionId)`
2. Llamarla con el Action ID de un disparo mágico

**Pros**:
- Más simple si encontramos la función correcta

**Contras**:
- Probablemente fuerza la animación completa de Clive
- No podríamos hacer que sean "automáticos" sin que Clive se anime

### Approach C: Memory Manipulation (State Injection)

**Objetivo**: Inyectar el estado de "disparo mágico activo" en memoria.

**Pasos**:
1. Encontrar la estructura en memoria cuando un proyectil existe
2. Clonar/crear esa estructura manualmente

**Pros**:
- No requiere hookear funciones de creación

**Contras**:
- Muy frágil, dependiente de versión del juego
- Requiere entender completamente la estructura del proyectil

### Approach D: Timeline/Animation Modding (Hybrid)

**Objetivo**: Crear un timeline custom que spawnee Dias.

**Pasos**:
1. Modificar el timeline del Perfect Dodge para que spawnee Dias
2. O crear una nueva acción con su timeline

**Pros**:
- Usa el sistema del juego de forma "legítima"
- Los VFX y sonidos funcionarían correctamente

**Contras**:
- Requiere modificar archivos del juego
- Menos dinámico (siempre el mismo comportamiento)
- Necesita aprender el sistema de timelines

---

## Recommendation & Next Steps

### Fase 1 (Corto Plazo): CheatEngine Investigation

**Objetivo**: Encontrar la función de creación de proyectiles

**Método**:
1. Disparar magia normal (Action 219) y poner breakpoint de acceso
2. Buscar la call stack cuando el proyectil se crea
3. Identificar patrones en la función de spawn

**Qué buscar**:
- Funciones que reciben ActionId como parámetro
- Funciones que escriben coordenadas (floats)
- Funciones tipo "Factory" o "Spawn"

### Fase 2 (Si A falla): Timeline Modding

**Objetivo**: Modificar el Perfect Dodge de Bahamut para spawnear magia

**Método**:
1. Aprender 010Editor + templates de mThund3R
2. Extraer timeline del Perfect Dodge Attack (Action 358-361)
3. Agregar elementos de spawn al timeline

### Fase 3 (Hybrid): Combinar ambos

- Timeline para el efecto visual/animación
- Function hooking para control condicional (solo cuando Diara buff activo)

---

## Quick Reference Card

```
┌────────────────────────────────────────────────────────────────┐
│                    QUICK REFERENCE                              │
├────────────────────────────────────────────────────────────────┤
│ Magic Shots: 218 (air), 219 (ground), 227 (charged)            │
│ Magic Burst: 199, 200, 201, 202                                │
│ Precision Shot: 222                                            │
├────────────────────────────────────────────────────────────────┤
│ Eikons: 0=Phoenix, 2=Garuda, 3=Titan, 4=Ramuh,                │
│         5=Shiva, 7=Odin, 8=Bahamut                             │
├────────────────────────────────────────────────────────────────┤
│ Clive IDs: 1, 2, 3, 4, 6, 8, 9, 10                             │
├────────────────────────────────────────────────────────────────┤
│ R15 Offsets: ActionId=+0xB0, Damage=+0x174                     │
├────────────────────────────────────────────────────────────────┤
│ Global Ptrs: EntityMgr=+0x1816CD0, PlayerState=+0x1816608      │
└────────────────────────────────────────────────────────────────┘
```

---

*This document is actively maintained. Update as new discoveries are made.*
