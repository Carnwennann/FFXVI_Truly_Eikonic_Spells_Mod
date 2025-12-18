# FFXVI Reverse Engineering Notes

Documentation of reverse-engineered game systems for modding reference.

---

## Table of Contents

1. [Reaction/Knockback System](#reaction-knockback-system)
2. [NEX Database Tables](#nex-database-tables)
3. [Game Functions (Ghidra)](#game-functions-ghidra)
4. [Data Structures](#data-structures)

---

## Reaction/Knockback System

The enemy hit reaction system consists of two main components:

| Offset | Name | Description |
|--------|------|-------------|
| `+0x15c` | **ReactionType** | Controls the ANIMATION the enemy plays |
| `+0x160` | **PushDirection** | Controls the DIRECTION/FORCE of the knockback (maps to `SystemMove.Key`) |

### Complete Data Flow

```
AttackParam.DamageReactionId  ──►  DamageReaction.Key
                                          │
                                          ▼
                                   DamageReaction.Unk4  ──►  SystemMove.Key
                                                                    │
                                                                    ▼
                                                            Physics Parameters:
                                                            - SidewaysMovement
                                                            - VerticalPush
                                                            - FallGravity
                                                            - DownwardMomentum
                                                            - etc.
```

### PushDirection → SystemMove Mapping

The `PushDirection` value written to `+0x160` is directly a **SystemMove.Key**. The game looks up physics parameters from the `SystemMove` NEX table.

---

## NEX Database Tables

### SystemMove (Physics Parameters)

**This is the core table for knockback physics.** Each `PushDirection` value (2-29) maps directly to a `SystemMove.Key`.

#### Table Schema

| Column | Type | Description |
|--------|------|-------------|
| Key | INTEGER | PushDirection value (used at +0x160) |
| DLCFlags | INTEGER | DLC requirement flags |
| SidewaysMovement | REAL | Horizontal knockback distance |
| SidewaysDuration | REAL | Duration of horizontal movement |
| VerticalPush | REAL | Initial upward velocity |
| VerticalInterpolation | REAL | Vertical smoothing factor |
| FallGravity | REAL | Gravity during fall (negative = faster fall) |
| ForwardPush | REAL | Forward momentum |
| ForwardPush2 | REAL | Secondary forward momentum |
| DownwardMomentum | REAL | Downward force (negative = slam down) |
| DownwardMomentum2 | REAL | Secondary downward force |
| Unk11 | REAL | Always 0.2 (friction?) |
| Unk12 | REAL | Height threshold? (20.0 for launches) |
| Unk14 | REAL | Delay before physics? |
| Unk19 | INTEGER | Flag (1 = special behavior) |
| Unk20 | INTEGER | Airborne check flag |
| Unk21 | INTEGER | Airborne state flag |

#### PushDirection Ranges with Physics Data

##### Range 2-5: Step Back (Ground Slide)
No vertical movement, only horizontal sliding.

| Key | SidewaysMovement | SidewaysDuration | Notes |
|-----|------------------|------------------|-------|
| 2 | 1.0 | 0.6 | Weakest |
| 3 | 2.0 | 0.6 | Weak |
| 4 | 3.0 | 0.6 | Medium |
| 5 | 4.0 | 0.6 | Strong |

##### Range 6-9: Parabola Push (Slight Lift)
Small vertical lift with horizontal movement. `Unk20=1, Unk21=1` (airborne flags).

| Key | SidewaysMovement | SidewaysDuration | VerticalPush | VerticalInterp |
|-----|------------------|------------------|--------------|----------------|
| 6 | 0.0 | 0.0 | 0.484 | 0.22 |
| 7 | 1.333 | 0.667 | 0.484 | 0.22 |
| 8 | 2.667 | 0.667 | 0.484 | 0.22 |
| 9 | 4.0 | 0.667 | 0.484 | 0.22 |

##### Range 10-13: Slam Downwards
Only `Unk20=1` set. Minimal physics - relies on animation.

| Key | SidewaysMovement | VerticalPush | Notes |
|-----|------------------|--------------|-------|
| 10-13 | 0.0 | 0.0 | Animation-driven slam |

##### Range 14-17: Launch Back + Up
Strong horizontal + vertical launch. `Unk12=20.0`, `Unk21=1`.

| Key | SidewaysMovement | SidewaysDuration | VerticalPush | VerticalInterp |
|-----|------------------|------------------|--------------|----------------|
| 14 | 5.0 | 1.0 | 0.625 | 0.25 |
| 15 | 7.5 | 1.0 | 0.9 | 0.30 |
| 16 | 10.0 | 1.0 | 1.225 | 0.35 |
| 17 | 12.5 | 1.0 | 1.6 | 0.40 |

##### Range 18-21: Launch Upwards (Vertical)
Primarily vertical launch with minimal horizontal. `Unk12=20.0`, `Unk14=0.166`.

| Key | SidewaysMovement | VerticalPush | VerticalInterp |
|-----|------------------|--------------|----------------|
| 18 | 1.0 | 1.25 | 0.354 |
| 19 | 1.0 | 2.25 | 0.474 |
| 20 | 1.0 | 3.25 | 0.570 |
| 21 | 1.0 | 4.25 | 0.652 |

##### Range 22-25: Slam Down Set 2
Uses **negative DownwardMomentum** for forced downward movement.

| Key | DownwardMomentum | DownwardMomentum2 | Notes |
|-----|------------------|-------------------|-------|
| 22 | -15.0 | -20.0 | Weakest slam |
| 23 | -20.0 | -20.0 | Weak |
| 24 | -25.0 | -20.0 | Medium |
| 25 | -30.0 | -20.0 | Strong slam |

##### Range 26-29: Launch Back + Down
Short duration horizontal movement, no vertical.

| Key | SidewaysMovement | SidewaysDuration |
|-----|------------------|------------------|
| 26 | 0.8 | 0.4 |
| 27 | 2.6 | 0.4 |
| 28 | 4.4 | 0.4 |
| 29 | 6.2 | 0.4 |

---

### DamageReaction

Links attack parameters to reaction behavior.

| Column | Description |
|--------|-------------|
| Key | Reaction ID (referenced by AttackParam.DamageReactionId) |
| Unk2 | Priority/weight? |
| Unk4 | **SystemMove.Key** reference for physics lookup |

#### Example Mappings

| DamageReaction.Key | Unk4 (SystemMove.Key) | Description |
|--------------------|----------------------|-------------|
| 2 | 504 | Light hit reaction |
| 3 | 505 | Medium hit reaction |
| 4 | 506 | Heavy hit reaction |
| 5 | 507 | Large knockback |
| 6 | 508 | Launch reaction |
| 7 | 509 | Strong launch |

---

### DamageReactionSize

Maps enemy SIZE to appropriate reaction type. Uses composite key (Key, Key2).

| Column | Description |
|--------|-------------|
| Key | Enemy size category (1-4) |
| Key2 | Variant (0=ground, 1=air, 2=other) |
| Unk4+ | PushDirection values for different attack strengths |

Example: Size 1 enemy, ground variant (Key=1, Key2=0):
- Unk4=2 (step back)
- Unk5=34, Unk6=34 (higher for stronger attacks)

---

### AttackParam

Defines attack properties including damage reaction.

| Column | Description |
|--------|-------------|
| Key | Attack ID |
| DamageReactionId | References `DamageReaction.Key` |
| GuardReactionId | Reaction when blocked |
| Comment | Human-readable attack name |

---

## Game Functions (Ghidra)

Base address assumption: `0x140000000`

### TriggerReactionHit - `FUN_140596f44` (Offset: `0x596F44`)

Entry point for processing hit reactions. Validates and stores reaction data.

```c
void TriggerReactionHit(void* reactionHandler, void* attackData);
```

**Key Operations:**
- Validates handler state at `handler[0x10]`
- Copies attack data to internal structure
- Calls update chain

---

### Reaction Update/Tick - `FUN_140595008` (Offset: `0x595008`)

Main update loop for reaction handlers. Called every frame during active reaction.

**VTable Location:** Handler base + 0x08 points to this function.

---

### Reaction Dispatcher/Factory - `FUN_140599e24` (Offset: `0x599E24`)

Creates appropriate reaction handler based on type ID.

```c
void* CreateReactionHandler(int reactionTypeId);
```

**Reaction Type IDs:**
- Different IDs create different VTable handlers
- Each handler type has specialized physics behavior

---

### Physics Update - `FUN_14024cc14` (Offset: `0x24CC14`)

**THE CORE PHYSICS FUNCTION** - Applies knockback velocity each frame.

**VTable:** Located at `PTR_FUN_1415ba8c0`, offset +0x08 = this function.

#### Physics Parameter Structure (from `param_1[0xe]`)

```c
struct PhysicsParams {
    // +0x00: VTable pointer
    float duration;           // +0x08: Time for calculation
    // ...
    float baseVelocity;       // +0x18: Base knockback speed
    float deceleration;       // +0x1c: Velocity decay rate
    // ...
    float animationFrame;     // +0x28: Current animation data
    // ...
    float totalDuration;      // +0x3c: Total reaction duration
    // ...
    byte groundedFlag;        // +0x52: Is enemy grounded?
    // ...
    float scaleFactor;        // +0x80: Multiplier for physics
};
```

#### Pseudocode Summary

```c
void PhysicsUpdate(void* handler) {
    PhysicsParams* params = handler[0xe];
    
    float elapsed = GetElapsedTime();
    float velocity = params->baseVelocity;
    float decel = params->deceleration;
    
    // Apply velocity with deceleration
    float currentVel = velocity - (decel * elapsed);
    
    // Update position based on velocity and direction
    UpdatePosition(handler, currentVel);
    
    // Check if grounded
    if (params->groundedFlag) {
        ApplyGroundFriction(handler);
    }
    
    // Check duration
    if (elapsed >= params->totalDuration) {
        EndReaction(handler);
    }
}
```

---

### Data Copy Function - `FUN_14024c990` (Offset: `0x24C990`)

Copies attack struct data to reaction handler during initialization.

---

## Data Structures

### Reaction Handler Structure

```c
struct ReactionHandler {
    void* vtable;              // +0x00: VTable pointer
    // ...
    uint32_t state;            // +0x10: Handler state
    // ...
    uint32_t reactionType;     // +0x15c: Animation type (0-12)
    uint32_t pushDirection;    // +0x160: SystemMove.Key (physics lookup)
    // ...
    PhysicsParams* physics;    // +0xe * 8: Physics parameter pointer
};
```

### VTable Layout (Offset from base)

| Offset | Function | Description |
|--------|----------|-------------|
| +0x00 | Destructor | Cleanup |
| +0x08 | Update | Physics update (FUN_14024cc14) |
| +0x40 | Unknown | 0x578680 |
| +0x48 | Unknown | 0x52700 |
| +0x68 | Unknown | 0x5786B8 |
| +0x70 | Unknown | 0x5786C4 |
| +0x88 | Unknown | 0x595480 |

---

## Global Data

### Physics Parameter Table - `DAT_1427438b8`

Global table containing base physics parameters. Indexed by SystemMove.Key.

---

## Useful SQL Queries

```sql
-- Get all SystemMove physics for standard PushDirection values
SELECT * FROM SystemMove WHERE Key BETWEEN 2 AND 30;

-- Find what physics a specific attack uses
SELECT ap.Key, ap.Comment, ap.DamageReactionId, dr.Unk4 as SystemMoveKey, sm.*
FROM AttackParam ap
JOIN DamageReaction dr ON ap.DamageReactionId = dr.Key
JOIN SystemMove sm ON dr.Unk4 = sm.Key
WHERE ap.Comment LIKE '%Rising%';

-- Get reaction mapping for enemy size
SELECT * FROM DamageReactionSize WHERE Key = 1; -- Size 1 enemies
```

---

## Modding Notes

### Setting Custom Knockback

To apply custom knockback in code:

```csharp
// In reaction hook:
// reactionType controls ANIMATION (0-12)
// pushDirection controls PHYSICS via SystemMove lookup (2-29 standard range)

*(int*)(reactionStruct + 0x15c) = 7;   // Fall face down animation
*(int*)(reactionStruct + 0x160) = 17;  // Strong launch back+up physics
```

### Creating New Physics Types

New physics can be added by:
1. Adding entries to `SystemMove` table in NEX database
2. Using those Keys as PushDirection values

Or by hooking `FUN_14024cc14` and modifying physics parameters directly.

---

## Version History

- **2024-12-14**: Initial documentation of reaction system
  - Mapped SystemMove table structure
  - Identified physics update function
  - Documented PushDirection → SystemMove relationship
