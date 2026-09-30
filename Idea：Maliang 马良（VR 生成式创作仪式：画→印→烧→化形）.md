---
base: "[[Ideas.base]]"
Type: "Idea"
tags:
  - VR
  - generative-ai
  - 3d-generation
  - tripo-ai
  - world-labs
  - interaction-design
  - ai-native-interaction
status: 概念阶段
date: 2026-09-29
---
> **与 [[Idea：马良（VR驱动的3D Gaussian World生成与演化系统）]] 的关系**：同一"神笔马良"母题的第二版。前者是以 3DGS 闭环世界演化为核心的**研究框架**；本篇是以仪式化交互为核心的**体验 / 产品设计**，生成后端换成 Tripo AI（物）+ World Labs（境）。

## 一句话概括

一个受《神笔马良》启发的 VR 生成式 AI 创作体验：把整套 AI 工作流隐藏成一场存在于 VR 世界内部的创作仪式——**画 → 印 → 烧 → 化形**。玩家不输入 Prompt、不点 Generate、不看 Loading Bar，只是拿起毛笔画出想象，然后让它成为现实。

> Maliang — Where imagination becomes reality.

---

## 1. 核心命题

> **如果使用生成式 AI 不再像是在操作一个 AI 工具，而更像是在施展魔法，会是什么体验？**

今天的生成式 3D 工具普遍遵循同一套交互逻辑：

```
输入 Prompt → 上传图片 → 点击 Generate → 等待 Loading → 下载/加载结果
```

Maliang 要把这条链路整体**消解进世界里的实体物件**。

---

## 2. VR 场景

玩家进入一个东方奇幻氛围的 VR 创作空间。桌面上摆放着：

**空白卷轴、毛笔、不同颜色的颜料、两枚印章，以及一簇燃烧的烛火。**

整个 AI 创作流程都通过这些真实存在于世界中的物件完成，而**不是传统的二维 UI**。

玩家先拿起一张卷轴展开——卷轴展开有完整动画表现（可用 Blender MCP 辅助制作展开 / 收起动画）。展开之后，卷轴就是玩家的画布。

---

## 3. 画：Draw

玩家拿起毛笔，在颜料中蘸取颜色，直接在卷轴上绘画。

**这里并不要求玩家具有绘画能力。** 可以画一把很简单的剑、一只龙、一栋房子，也可以只用几笔画出山、湖泊、月亮和森林。例如：

- 一座雪山、一轮月亮、一座山顶上的塔
- 一把蓝色的剑，剑柄上有一颗红色宝石

> 目标不是让玩家创作精确的 Concept Art，而是**捕捉玩家的 imagination**。AI 后续负责理解这些简单的视觉表达。

---

## 4. 印：Seal —— 用印章代替 Model Selection

画完之后，玩家需要决定：**我要让这幅画变成"物"，还是变成"世界"？**

面前有两枚中国篆刻风格的印章，盖下后在卷轴上留下红色印记。

| 印章 | 含义 | 对应后端 |
|---|---|---|
| **「物」印** | Object Generation —— 我要让画里的东西来到我面前（剑、龙、杯子、汽车、建筑模型、幻想生物……） | Tripo AI Pipeline |
| **「境」印** | World Generation —— 我要进入画里的世界（雪山、森林、海岛、城堡、外星世界、幻想山谷……） | World Labs Pipeline |

同一幅画可以有两种解释。比如玩家画了一座城堡：

- 盖「物」印 → 一个可以摆在面前观察的 3D Model
- 盖「境」印 → 一个玩家真正可以走进去的 3D World

> **是玩家、而不是 AI，决定自己的创作意图。**

---

## 5. 烧：Burn —— 用点火代替 Generate Button

盖章之后，玩家拿起卷轴靠近烛火。卷轴被点燃。

**这一刻就是整个体验中的 Generate Button，但玩家永远不会看到一个真正的 Generate Button。**

火焰沿卷轴蔓延，通过 Burning / Dissolve Shader 逐渐燃烧；卷轴脱离玩家的手、取消重力，缓缓悬浮到空中，火焰、灰烬、粒子与环境效果逐渐增强。

- 对玩家来说：**魔法正在生效。**
- 在后台：**AI Generation Pipeline 已经开始运行。**

---

## 6. 「物」：Tripo AI Pipeline

系统截取玩家在卷轴上的画作，发送给视觉模型。GPT Vision 分析：

- 这个物体是什么
- 有哪些重要的形状特征
- 使用了什么颜色
- 玩家画了哪些具有辨识度的细节
- 哪些视觉特征必须在最终模型中保留

随后根据原画生成更完整的 **Object Prompt** 和干净背景的 **Reference Image**；若 pipeline 允许，进一步生成 Front / Side / Back 多方向参考图，再传入 **Tripo AI API** 生成完整 3D Asset。

生成完成后模型被动态加载回 VR 场景——此时卷轴刚好燃烧殆尽，灰烬散开，原本悬浮卷轴的位置出现玩家刚画出的 3D 物体。玩家可以直接**伸手 → 抓住 → 拿起来**。

> 几十秒之前它只是卷轴上的几笔颜料，现在玩家真的把这把剑握在手里。
> **Tripo turns your painting into something you can hold.**

---

## 7. 「境」：World Labs Pipeline

视觉模型先理解玩家画中的**环境、空间结构、建筑、地貌、天气、时间、颜色与整体氛围**。

例如玩家画的是「雪山 + 月亮 + 湖泊 + 山顶上的塔」，AI 将其转化为更完整的 **Environment Prompt** 并生成适合 World Generation 的 Reference Image，随后调用 **World Labs API** 生成对应的 3D / Spatial World。

生成过程中玩家看到的仍然只是正在燃烧的卷轴。当 generation 接近完成时卷轴也燃烧到最后，最终完全化为灰烬——玩家刚刚画出的世界出现在面前，可以真正走进去。

> 几十秒之前玩家在纸上画了一座山；几十秒之后玩家站在了那座山里。
> **World Labs turns your painting into somewhere you can enter.**

---

## 8. 燃烧就是 Loading

最重要的 Interaction Design 决策：

> **Burning Shader = Loading Indicator**

AI Generation 最大的体验问题之一就是等待。传统 AI 产品告诉用户 `Generating… 37%`；Maliang 不显示 Loading Bar，而把生成进度映射到卷轴燃烧过程：

| 进度 | 燃烧阶段 |
|---|---|
| 0% | 卷轴刚刚被点燃 |
| 30% | 火焰开始扩散 |
| 60% | 卷轴大面积燃烧并悬浮 |
| 90% | 只剩少量残片和灰烬 |
| 100% | 卷轴完全燃尽，生成内容出现 |

若实际 API generation 时间不可预测，可通过控制燃烧阶段、火焰、粒子与最终 materialization animation 来吸收等待。

> 因此 **AI latency 不再只是一个工程限制，它成为整个魔法仪式的一部分。**

---

## 9. 完整 Interaction Loop

```
展开卷轴 → 拿起毛笔 → 蘸取颜料 → 画出自己的想象
   → 选择印章（「物」→ Tripo AI ／「境」→ World Labs）
   → 盖章 → 用烛火点燃卷轴 → 卷轴悬浮并逐渐燃烧
   → AI 在后台理解并生成 → 卷轴燃尽 → 画作成为现实
```

浓缩成四个动作：

> **Draw → Seal → Burn → Manifest**
> 画 · 印 · 燃 · 现

---

## 10. 核心理念：Generative AI Native Interaction

Maliang 并不是单纯把一个 AI 3D Generator 放进 VR。它探索的是：

> **当生成式 AI 足够强大之后，我们是否还需要像"使用软件"一样使用 AI？**

Prompt、Generate Button、Loading Bar、Asset Import 其实都是计算机时代留下来的 Interface。Maliang 让它们逐一消失：

| 传统 Interface | 在 Maliang 中的化身 |
|---|---|
| Prompt | 玩家的绘画 |
| Model Selection | 两枚印章 |
| Generate Button | 点燃卷轴的动作 |
| Loading Bar | 燃烧的火焰 |
| Output 文件 | 直接成为玩家所处现实的一部分 |

> AI 存在于整个体验背后，但玩家几乎不需要意识到自己正在"操作 AI"。玩家只是画出自己的想象，然后施展魔法。

---

## 11. Tripo AI × World Labs：两种尺度的创造

- **Tripo AI —— Things you can hold.** 创造存在于世界中的"物"。
- **World Labs —— Places you can enter.** 创造玩家能够进入的"境"。

一个负责创造世界中的东西，一个负责创造东西所在的世界，而连接两者的是玩家自己的想象。

### 备选 Tagline

> Paint what you imagine. / Seal your intention. / Burn the scroll. / Bring imagination into reality.

更简洁的版本：

> **Draw it. Seal it. Burn it. Bring it to life.**
