# Native motion inventory (2026-09-24)

The main female controller is `lib/ani/mot/1/000_00.unity3d` (`f_00`).
It contains 2,136 AnimationClip objects and 746 distinct `Base Layer.*`
state paths. These numbers include variants and transitions; they are **not**
2,136 independent conversation gestures.

| Controller group | State paths | Use/context |
| --- | ---: | --- |
| `adv` | 148 | 74 `in` + 74 `Loop` conversation poses |
| `tachi` | 52 | standing/map actions and their sub-states |
| `Idle` | 46 | personality poses plus short-movie idle states |
| `isu` | 29 | chair actions |
| `tukueisu` | 19 | table/chair actions |
| `yuka` | 10 | floor/crouching actions |
| `ne` | 8 | lying actions |
| `tokushu 0` | 28 | special-location actions |
| `daturyoku` | 16 | slumped actions |
| `water_Reaction` | 357 | water/swim variants |
| `event_adv` | 30 | event poses |
| `H_event_adv` | 3 | H-event poses |

The `adv` group further splits into 40 chair, 20 table/chair, 8 floor,
24 lying, 52 special-location, and 4 nominal standing state paths.
The nominal standing group includes a state whose list label says `椅子待機`,
so category names alone cannot establish stage compatibility.

The native list `lib/ani/lis/m/000_00.unity3d` (`anim_f_000`) labels additional
standing/map actions: walking (`al_f_act_00_02`), shoreline walking (`00_06`),
foot splashing (`00_10`), licking ice cream (`00_18`), drinking (`00_20`),
eating (`00_22`), souvenir/plant inspection (`00_24`), and waterfall viewing
(`00_26`). Some require character translation, props, water, or a location;
they have not been added to the default AI allow-list on asset evidence alone.
The same list labels `00_28/29`, `00_32/33`, and `00_36/37` as left/right
turns while holding ice cream, a drink, and food respectively.

`lib/ani/mot/1/005_00.unity3d` is a small supplemental controller with nine
personality-03 pose variants/reactions, not a generic gesture library.

The default catalog currently exposes 12 state IDs tested on the dedicated
character stage. There are clearly more assets to explore. To add seated,
floor, lying, water, or prop actions responsibly, the stage needs corresponding
positioning/props and runtime tests. A string search of the main animation
list found no explicit `手を振る` or `うなずく` label; this does not prove that
no visually similar clip exists anywhere in the game.
