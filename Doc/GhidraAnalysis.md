# Deep Dive Analysis: FireContextualMagicProjectile (0x56E0F0)

## Function Signature
`void FireContextualMagicProjectile(longlong magicManager /* RCX */, longlong projectileData /* RDX - UNUSED */)`

## Variable Analysis (Ghidra -> Logical Name)

| Ghidra Var | Logical Name | Description |
| :--- | :--- | :--- |
| `param_1` | `MagicManager*` | Pointer to the Magic Manager singleton (RCX). |
| `iVar4` | `ShotType` / `ActionId` | **Dual Purpose Variable**.<br>1. Initially holds the **Shot Type** read from config (1=Normal, 2=Charged, etc.).<br>2. Later holds the final **Action ID** (e.g., 227) passed to the spawning function. |
| `plVar8` | `PlayerState*` | Pointer to the Player's state/entity. Retrieved via `FUN_14077ee10`. Used to call virtual methods (vtable) for executing specific shot logic. |
| `uVar6` | `TimelineContext*` | Pointer to the current animation timeline/context. Retrieved from `magicManager + 0x28`. |
| `lVar7` | `AttackData*` | Pointer to attack definition/template data. |
| `iVar5` | `TimingCheckResult` | Result of a timing check (likely for Magic Burst window). Used in Case 4. |
| `[param_1 + 0x38]` | `MagicConfig*` | Pointer to the input configuration structure. |
| `[config + 0x10]` | `RequestedShotType` | The input that determines what the player *wants* to fire (1, 2, 3, 4). |
| `[config + 0x18]` | `MagicBurstActionId` | Action ID for Magic Burst (e.g., 218/219). |
| `[config + 0x1C]` | `ChargedShotActionId` | Action ID for Charged Shot (e.g., 227). |

## Logic Flow Breakdown

### 1. Initialization & Config Read
```c
// Reads the config pointer from MagicManager
MagicConfig* config = *(longlong*)(magicManager + 0x38);

// Reads the requested shot type (1, 2, 3, 4)
// This is what we modified in the experiment to force Charged Shot!
int shotType = *(int*)(config + 0x10);
```

### 2. Branching Logic (The "Switch")

#### Case 1: Normal Shot
```c
if (shotType == 1) {
    // Get Player State
    PlayerState* player = GetPlayerState(...);
    if (player == null) return;
    
    // Call Virtual Function +0x78 (ExecuteNormalShot)
    // This likely sets up the animation and state for a standard shot.
    player->vtable[0x78](player);
}
```

#### Case 2: Charged Shot (Megafulgor/Flare)
```c
else if (shotType == 2) {
    PlayerState* player = GetPlayerState(...);
    
    // Check charge level? (Returns 7?)
    int check = CheckChargeLevel(...); 
    
    // Call Virtual Function +0x80 (ExecuteChargedShot)
    // Takes a boolean param (1 < check)
    player->vtable[0x80](player, check > 1);
}
```

#### Case 3: Precision Counter? (Unconfirmed)
```c
else if (shotType == 3) {
    PlayerState* player = GetPlayerState(...);
    // Call Virtual Function +0x88
    player->vtable[0x88](player);
}
```

#### Case 4: Complex / Magic Burst Logic
This is the most interesting case. It handles situations where the shot type isn't explicitly 1, 2, or 3, but depends on timing (like Magic Burst).

```c
else {
    if (shotType != 4) return; // Exit if unknown

    // ... Complex timeline/animation checks ...
    
    // Check timing window
    int timingResult = CheckMagicTiming(...);
    
    // DECISION TREE:
    
    // If timing is right for Magic Burst (Result == 1)
    if (timingResult == 1) {
        // Load Action ID from +0x18 (Magic Burst ID)
        finalActionId = *(int*)(config + 0x18);
    }
    // If timing indicates Charged Shot (Result > 1)
    else if (timingResult > 1) {
        // Load Action ID from +0x1C (Charged Shot ID)
        finalActionId = *(int*)(config + 0x1C);
    }
}
```

### 3. Execution (The "Spawn")
If `iVar4` (ActionId) is set (non-zero), the function proceeds to actually create the projectile.

```c
if (finalActionId != 0) {
    // ... Massive stack setup ...
    
    // Calls the function that actually spawns the attack/projectile
    // FUN_1406d7908(..., finalActionId, ...);
    SpawnAttack(..., finalActionId, ...);
}
```

## Conclusions for Modding

1.  **Control Point**: `[RCX + 0x38] + 0x10` is the master switch. Writing `2` here forces the game to execute the "Charged Shot" logic branch, regardless of actual button press duration.
2.  **Suppression**: By returning early or preventing the call to `SpawnAttack` (or the vtable calls), we can suppress the effect. Our current method works by intercepting the call *before* this function runs, reading the config, and if it's a Charged Shot, we just don't call `OriginalFunction`.
3.  **Magic Burst**: If we wanted to force a Magic Burst, we would likely need to set `shotType = 4` AND ensure the timing check returns 1, OR manually overwrite the ActionID in the complex block (harder).
