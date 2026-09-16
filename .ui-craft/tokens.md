> 2026-09-15 文档核对：当前基线为 0.24.0。每日复盘复用 `paper`/`card`、`accent.tint`/`accent.soft`、`ink`/`muted` 与既有危险次要样式；日期导航、编辑、导出和删除按钮至少 36 DIP，保存/错误与统计使用文字和 polite live region，不新增色值、字体或动效。当前实现与验证边界见 [技术设计](../docs/TECHNICAL_DESIGN.md) 和 [当前状态](../docs/CURRENT_STATUS.md)。

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
| destructive secondary action | card surface + error text/border; pale error tint on hover |

## Component

- Tool popover: 20px outer radius, paper surface, near-invisible edge and one warm neutral shadow.
- Search field and inline selector: 11px radius, white surface, at least 40px row height, solid accent focus ring; compact segmented selectors share one soft group surface and retain a non-color selected marker.
- Scrollbar: 10px interaction lane with a centered 6px warm-accent thumb, 30px minimum thumb height, quiet soft rail, and stronger hover/drag feedback.
- Buttons: 10px radius, 44px normal height; compact text controls use at least 36px; tool-window title icon controls use 36×36, while larger form icon controls retain 44×44.
- Cards: 14px radius, white surface, `0 2px 12px` equivalent warm shadow; primary result/shortcut hover lifts 2px without changing layout.
- Pet bubble: 12px radius, paper surface, concise one-line text; appears for hover/action feedback, low-frequency idle phrases, and configured reminder messages.
- Navigation: compact four-item top strip; selected item is a raised white pill with warm-orange text.
- Search result actions: 10px radius, `accent.tint` surface with `accent.soft` border, 11px selected-summary text, 36px minimum text-button height, and WrapPanel fallback within the fixed-width popover.
- Shortcut management: 48×48 card, 24×24 always-discoverable overflow target; invalid state combines `status.error`, a warning glyph, tooltip, and automation name.
- Progressive disclosure: section-card or accent-tint surface with a visible Expander header and short consequence text; collapsed state never mutates business data.
- Settings directory: five equal-width visible choices using the existing underline, weight, and accent selected markers; selection scrolls and focuses the named section.
- Smart update route: `accent.tint` status strip with a visible “自动” badge, one concise route/status sentence, polite live-region updates, and no user-editable proxy or credential fields.
- Notification focus: visible inline query and segmented filters precede the native single-select list; the selected target uses one `accent.tint` action strip. Quiet-hour fields and presets remain drafts until explicit save, while validation, save state, history count, and delivery feedback stay visible in text.
