# 多功能 AI 桌宠 · 设计令牌

## Primitive

| 类别 | 名称 | 值 |
| --- | --- | --- |
| neutral | paper | `#FFFCF8` |
| neutral | canvas | `#FAF7F2` |
| neutral | card | `#FFFFFF` |
| neutral | soft | `#F3EDE6` |
| neutral | line | `#E6DBCF` |
| neutral | muted | `#786F67` |
| neutral | ink | `#33302E` |
| accent | tint | `#FCEADE` |
| accent | soft | `#F7DCC9` |
| accent | base | `#B45E32` |
| accent | hover | `#A95129` |
| accent | pressed | `#934622` |
| status | success | `#4F7A60` |
| status | error | `#AE4F4F` |
| spacing | scale | `4, 8, 12, 16, 24, 32` px |
| radius | button/input/card/popover | `10 / 11 / 14 / 20` px |
| type | caption/body/title | `12 / 14 / 20` px |
| motion | fast/normal | `120 / 220` ms |

## Semantic

| 角色 | 引用 |
| --- | --- |
| window background | paper |
| inset background | canvas |
| elevated surface | card + warm neutral shadow |
| hover background | accent.tint or canvas |
| primary text | ink |
| secondary text | muted |
| focus / primary action | accent.base |
| selected navigation | card + accent.pressed text |
| subtle border | line at 40–70% opacity, only where needed |

## Component

- Tool popover: 20px outer radius, paper surface, near-invisible edge and one warm neutral shadow.
- Search field and dropdown: 11px radius, white surface, 44px height, accent focus ring; no native gray-gradient chrome.
- Buttons: 10px radius, 44px normal height; icon-only controls use a 44×44 hit target.
- Cards: 14px radius, white surface, `0 2px 12px` equivalent warm shadow; primary result/shortcut hover lifts 2px without changing layout.
- Pet bubble: 12px radius, paper surface, concise one-line text; only appears for hover/action feedback.
- Navigation: compact four-item top strip; selected item is a raised white pill with warm-orange text.
