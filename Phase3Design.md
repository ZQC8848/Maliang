---
Type: Design
project: Maliang 马良
phase: 阶段 3(M2)「物」AI 生成管线
status: 已定稿,待实现
date: 2026-10-01
related: "TechPlan.md, PlanTodo.md"
---

# 阶段 3 设计:「物」AI 生成管线(建模、绑骨、动画、声音、失败)

本文档规定把盖过章的水墨画变成一个「活物」的 AI 管线:先由 GPT 看图,判断画的是什么并规划后续步骤;再由 Tripo 建模(能动的就绑骨、套动作);同时由音效模型生成它的声音。另外规定了「GPT 认不出来」时的游戏内失败表现,以及作品的本地持久化和回放(第 8 节)。第 1 节列出的所有决定均已在设计讨论中确认。

**文字规则:游戏内玩家能看到的所有文字(UI、提示语)一律用英文,游戏里不需要任何中文字体。** 本文档本身用中文。

---

## 1. 决定一览

| # | 主题 | 决定 |
|---|---|---|
| D1 | 大脑 | GPT 看图一次调用,用结构化输出规划整个任务:识别、提示词、绑骨、动画、声音。 |
| D2 | 识别尺度 | 偏宽松。只有完全说不出是什么时才判失败(乱涂、几乎空白、多个形状挤在一起分不开)。鼓励大胆解读。 |
| D3 | 置信度 | 不用数字阈值。GPT 按写清楚的标准直接给出 `ok` / `fail`。 |
| D4 | 图片精修 | OpenAI 图片编辑,用 `gpt-image-2.5-sunburst`(精确度优先)。出错就跳过,直接把原始水墨图交给 Tripo。 |
| D5 | 建模模型 | 会动的生物用 Tripo `P1-20260311`(低面数,对 VR 友好);静态物体用 `v3.1-20260211`。P1 的模型质量要先通过实测(第 13 节)。 |
| D6 | 绑骨把关 | 双重判断:GPT 判断「该不该动」,Tripo 免费预检(rig-check)判断「能不能动」,两边都同意才绑骨。骨架类型以预检建议为准。 |
| D7 | 绑骨版本 | 人形用 `v1.0-20240301`(90 多个动作);其他生物用 `v2.5-20260210`;鸟类不绑骨(没有预设动作),改用程序动画。 |
| D8 | 声音 | ElevenLabs Sound Effects v2,和建模并行生成。由 GPT 判断要不要声音、提示词、类型(一次性或循环)和触发时机。不该出声的物体不生成。 |
| D9 | 在哪里跑 | 直接在 Unity 里用 C# 实现,放在 `Assets/Maliang/Api/`。黑客松版本不另起服务器。 |
| D10 | GPT 文本模型 | 实现时用我们的 key 调 `GET /v1/models` 查可用型号再定(2026 年 OpenAI 型号名变化频繁)。 |
| D11 | 揭晓时机 | 印章一落就把图发给 GPT,结果留到燃烧时再演出来。 |
| D12 | 燃烧等待点 | 燃烧推进到 40% 时,如果 GPT 结果还没回来,火焰放慢、停在 40% 等待;成功则继续烧,失败则开始失败演出。 |
| D13 | 失败提示语 | 按失败原因从预先写好的英文句子里挑,GPT 不写任何给玩家看的文字。 |
| D14 | 掉下来的残卷 | 变成受物理控制的物体:可以捡、可以扔,约 12 秒后化成灰消失。 |
| D15 | 技术性失败 | 断网、超时、鉴权、额度、内容审核、Tripo 生成失败,都走同一套失败演出,只换提示语。绑骨或套动作失败不算失败:物体照样出现,只是不会动。 |
| D16 | 作品库 | 每次成功生成的物体或场景都完整存到本地:画面(墨迹层 + 印章层)、模型或 splat、声音、动画、清单。失败的不存。 |
| D17 | 抽屉 | 左抽屉放「物」,右抽屉放「境」,每个抽屉显示最新 6 卷;更早的作品仍保存在硬盘上。 |
| D18 | 回放卷轴 | 从抽屉拿出,放到画画位,展开后是原画和原印章;锁定,不能画、不能再盖印;展开后自动浮空。 |
| D19 | 回放燃烧 | 固定 10 秒燃烧动画,不调用任何 API;烧完载入本地的模型或 splat。 |
| D20 | 回放不消耗 | 烧完后卷轴回到抽屉原位,作品永久保留。 |
| D21 | 写入方式 | 先写临时目录,全部文件就绪后整体改名为正式目录,中途中断不会留下半个作品。 |

---

## 2. 管线总览

```
印章落下
  |
  |-- 导出水墨 PNG(InkExporter)----------------------------------------------+
  |                                                                           |
  v                                                                           |
[1] GPT 看图(结构化输出)                                                     |
      输入:水墨 PNG、印章类型、能力表(第 6 节)                                |
      输出:VisionPlan(status 为 ok 或 fail)                                  |
  |                                                                           |
  |-- fail --> 结果 = 失败(原因) --------------------------------------> 失败流程(第 7 节)
  |
  |-- ok ----+--------------------------------------------------+
  |          |                                                  |
  v          v                                                  |
[2] 图片精修(gpt-image-2.5-sunburst,编辑)               [5] 声音(ElevenLabs SFX v2)
      水墨 PNG -> 干净的单主体参考图                           提示词、时长、是否循环
  |                                                         -> MP3 音频(或者没有)
  v                                                                 |
[3] Tripo 图生模型                                                  |
      生物用 P1,静态物体用 v3.1                                    |
      任务一成功立刻下载 GLB(链接约 5 分钟后失效)                  |
  |                                                                 |
  v                                                                 |
[4] 绑骨把关                                                        |
      GPT 认为不该动        --> 静态 GLB                            |
      Tripo 预检:不能绑     --> 静态 GLB                            |
      绑骨(人形 v1.0 / 其他 v2.5)--> 套用预设动作                  |
      绑骨或套动作出错     --> 静态 GLB(不算失败)                  |
  |                                                                 |
  v                                                                 v
[6] Unity:载入 GLB、统一尺寸、碰撞体、可抓取、骨骼动画或程序动画、挂上声音
  |
  v
燃烧结束时在卷轴悬停的位置化形
  |
  v
写入作品库(第 8 节),左抽屉里多出一卷
```

第 2~5 步只在结果为 `ok` 后才开始。声音(第 5 步)和第 2~4 步并行,永远不阻塞化形:如果化形时声音还没好,物体先无声出现,声音到了再挂上去。

---

## 3. 触发与时机

1. `ScrollRitual.OnSealed` 时导出水墨图(现有的 `InkCanvas.Export`),并启动一个 `ObjectJob`。
2. GPT 看图一般 5~10 秒返回,通常玩家还没把蜡烛拿过来就有结果了。
3. 结果先不揭晓,留到燃烧时再演(D11),这样成功和失败共用同一段仪式。
4. **燃烧等待点(D12)**:`_BurnProgress` 正常推进到 0.40。如果结果还没回来,火焰放慢成文火,停在 0.40「凝聚」。结果为 `ok` 就继续烧;结果为 `fail`,就从当前进度开始失败演出。
5. 结果为 `ok` 之后,燃烧就是第 2~4 步的加载条(TechPlan 第 7 节)。燃烧节奏属于阶段 4 的工作,这里不改。

---

## 4. GPT 看图

### 4.1 输入

- 导出的水墨 PNG(`InkExporter` 已按墨迹范围裁好)。
- 印章类型(这里是「物」;「境」在阶段 6 用另一套提示词)。
- 第 6 节的能力表,写进系统提示词,让 GPT 只规划 Tripo 真正做得到的事。

### 4.2 系统提示词要点

- 找出画中最可能的**单一主体**。水墨讲写意,几笔可能就是一座山、一只鸟、一条船。宁可大胆而合理地解读,也不要判失败。
- 只在以下四种情况返回 `fail`:
  - `unrecognizable`:说不出是什么;
  - `too_abstract`:只有笔触,没有主体;
  - `nearly_blank`:墨太少;
  - `crowded`:几个形状挤在一起,分不出一个主体。
- 把主体归入第 6 节中恰好一个类别。
- `refine_prompt`(精修提示词)要求:**不要水墨风格**(没有笔触、纸纹、晕染),而是干净、细节清楚、光线均匀、颜色自然的参考图,类似高质量游戏资产的渲染效果;保留主体本身和玩家画出的颜色、特征。单一主体,整个身体或整个物体完整入画,纯浅灰背景,不要文字、边框、印章、其他物体,手里不拿东西(除非那就是主体)。生物要自然站立、四肢分开且露出来(不要长袍遮住腿)、四分之三侧视;人形用放松的 A 姿势、双手空着。
- `model_prompt`:给 Tripo 的同一主体的简短描述,不要水墨风格。
- 动画:只给有预设动作的生物类别请求绑骨;动作只能从该类别的允许列表(6.2)里选。人形选 `idle` 加一个和主体相符的表现性动作。
- 声音:只有主体本来就会发声时才请求(马嘶、火噼啪、钟响);石头、印章、杯子和大多数植物不出声。提示词要具体,写清声源、动作、质感和距离;并给出类型(`oneshot` 一次性或 `loop` 循环)和触发时机。
- `size_m`:主体在房间里的合理尺寸,之后会再限制到玩家面前放得下的范围。

### 4.3 输出格式(严格结构化输出)

```json
{
  "status": "ok",
  "subject": "a galloping horse",
  "seen": "flowing strokes forming a horse with raised forelegs",
  "category": "quadruped",
  "refine_prompt": "...",
  "model_prompt": "...",
  "animate": {
    "wanted": true,
    "rig_type": "quadruped",
    "animations": ["preset:quadruped:walk"]
  },
  "sound": {
    "wanted": true,
    "prompt": "horse whinny, short, outdoor, mid distance",
    "kind": "oneshot",
    "trigger": "on_grab",
    "duration_s": 2.5
  },
  "size_m": 0.6
}
```

失败时:

```json
{
  "status": "fail",
  "reason": "unrecognizable",
  "seen": "a few crossing strokes with no clear subject"
}
```

字段规则:

| 字段 | 取值 |
|---|---|
| `status` | `ok`、`fail` |
| `reason`(仅失败时) | `unrecognizable`、`too_abstract`、`nearly_blank`、`crowded` |
| `category` | `biped`、`quadruped`、`hexapod`、`octopod`、`serpentine`、`aquatic`、`avian`、`object`、`plant`、`scenery` |
| `animate.rig_type` | 可绑骨的类别之一;`wanted` 为 false 时忽略 |
| `animate.animations` | 1~2 个,必须来自该类别的允许列表 |
| `sound.kind` | `oneshot`、`loop` |
| `sound.trigger` | `on_spawn`、`on_grab`、`loop` |
| `sound.duration_s` | 0.5~30 |
| `seen` | 自由文本,只写日志,不给玩家看 |

客户端会按允许列表校验返回内容,超出范围的直接丢掉。比如不认识的动作名会被删掉;如果一个动作都不剩,就按静态物体处理。

---

## 5. 第 2~6 步

### 5.1 图片精修(OpenAI)

- 接口:`POST /v1/images/edits`,模型 `gpt-image-2.5-sunburst`,输入为水墨 PNG,提示词为 `refine_prompt`。
- 尺寸 1024×1024,质量 `high`,输出 PNG。背景用不透明的纯色:虽然支持透明背景,但 Tripo 在干净的不透明背景上效果最好。
- 出错或超时:跳过这一步,把原始水墨 PNG 交给 Tripo。

### 5.2 Tripo 建模

- 沿用现有流程:`POST /v3/files` 上传 → `POST /v3/generation/image-to-model` → 轮询 `GET /v3/tasks/{id}`。
- 模型:`animate.wanted` 为 true 且类别可绑骨时用 `P1-20260311`,否则用 `v3.1-20260211`。
- 任务一成功立刻下载 GLB(输出链接约 5 分钟后失效)。

### 5.3 绑骨把关

| GPT 的 `animate.wanted` | Tripo 预检 | 处理 |
|---|---|---|
| false | 不调用 | 静态 |
| true | `riggable: false` | 静态 |
| true | `riggable: true`,类型 T | 按类型 T 绑骨(预检结果优先于 GPT 的 `rig_type`) |
| true,类别为 `avian` | 不调用 | 静态 + 程序飞行动画(没有鸟类预设动作) |

- 预检:`POST /v3/animations/rig-check`,输入建模任务的 task id,免费。
- 绑骨:`POST /v3/animations/rig`,人形 `model` = `v1.0-20240301`,其他 `v2.5-20260210`;`spec` = `tripo`,`out_format` = `glb`。约 25~30 积分,约 30 秒。
- 套动作:`POST /v3/animations/retarget`,`input` = 绑骨任务 id,`animation` = 一个动作,`out_format` = `glb`,`bake_animation` = true,`animate_in_place` = true(生物原地播放,不会走远)。
  - **每个动作单独请求一次**(实测 2026-10-01):一次请求里传两个动作,返回的 GLB 只含最后一个动作,但按两个计费。要两个动作时调用两次,载入时把第二个 GLB 的动画合并到第一个模型上(同一套骨骼)。
  - 动画名不可靠(四足返回 `preset:quadruped:walk`,人形返回 `bow`),代码里按顺序取,不按名字。
- 如果选的动作属于另一个绑骨版本(见 6.2),就丢掉,退回 `idle`(人形)或该类唯一的移动动作(其他)。
- 绑骨或套动作出错:用 5.2 下载的静态 GLB,不算失败(D15)。

### 5.4 声音(ElevenLabs)

- 接口:`POST https://api.elevenlabs.io/v1/sound-generation`,`model_id` = `eleven_text_to_sound_v2`。
- 参数:`text` = `sound.prompt`,`duration_seconds` = `sound.duration_s`,`loop` = (`kind` 为 `loop`),`prompt_influence` = 0.5;输出 `mp3_44100_128`。
- Unity:运行时按 `AudioType.MPEG` 解码;物体上挂 3D `AudioSource`(空间混合 1,对数衰减,最远约 6 米)。
- 触发:`on_spawn` 化形时播一次;`on_grab` 每次被抓时播(带短冷却);`loop` 一直循环播放。
- 任何错误:物体没有声音,不阻塞流程。

### 5.5 Unity 衔接

- 用 glTFast 运行时载入 GLB,包括其中烘焙的动画(需实测,见第 13 节)。
- 缩放到 `size_m`(限制在 0.15~1.2 米),放在卷轴悬停的位置;加碰撞体和 `XRGrabInteractable`(沿用 `GrabbableTool` 的行为,但松手不回原处)。
- 会动的:循环播放第一个动作;有两个动作时,被抓取时播第二个,然后回到第一个。
- 静态的或鸟类:挂 `ProceduralMotion` 组件(上下浮动、缓慢转动;鸟类加轻微扇翅摆动,在出现点附近盘旋)。

---

## 6. 能力表(交给 GPT)

### 6.1 类别

| 类别 | Tripo 骨架类型 | 绑骨版本 | 预设动作 | 举例 |
|---|---|---|---|---|
| `biped` | biped | v1.0-20240301 | 90 多个(见 6.2) | 人、仙人、猴王 |
| `quadruped` | quadruped | v2.5-20260210 | 只有走 | 马、鹿、虎、狗 |
| `hexapod` | hexapod | v2.5-20260210 | 只有走 | 甲虫、蚂蚁 |
| `octopod` | octopod | v2.5-20260210 | 只有走 | 蜘蛛、章鱼 |
| `serpentine` | serpentine | v2.5-20260210 | 只有前进 | 蛇、长龙 |
| `aquatic` | aquatic | v2.5-20260210 | 只有前进 | 鱼、锦鲤 |
| `avian` | avian(不使用) | — | 无 | 鹤、凤凰、燕子 |
| `object` | 不能绑骨 | — | — | 灯笼、花瓶、船、印章 |
| `plant` | 不能绑骨 | — | — | 荷花、松、竹 |
| `scenery` | 不能绑骨 | — | — | 亭子、石头、山 |

### 6.2 预设动作允许列表

- **绑骨 v1.0-20240301,人形**:`preset:idle`、`preset:walk`、`preset:run`、`preset:jump`、`preset:turn`、`preset:climb`、`preset:dive`、`preset:fall`、`preset:hurt`、`preset:slash`、`preset:shoot`,加上 v1.0 的扩展动作(afraid、agree、angry、bow、cast_a_spell、cheer、clap、cry、dance_01~dance_06、greet_01~greet_04、laugh、look_around、sing、sit、standing_relax、swim、victory_celebration、wait、wave_goodbye 等)。写提示词文件时,从 Tripo 套动作文档原样复制准确的名字。
- **绑骨 v2.5-20260210**:`preset:quadruped:walk`、`preset:hexapod:walk`、`preset:octopod:walk`、`preset:serpentine:march`、`preset:aquatic:march`。没有鸟类动作。

---

## 7. 失败流程

### 7.1 失败来源与提示语

提示语是游戏内文字,保持英文原句。

| 来源 | 原因代码 | 游戏内提示语(英文) |
|---|---|---|
| GPT 判 `fail` | `unrecognizable` | "The ink found no shape to become." |
| GPT 判 `fail` | `too_abstract` | "The strokes wander. No form answers them." |
| GPT 判 `fail` | `nearly_blank` | "Too little ink to hold a spirit." |
| GPT 判 `fail` | `crowded` | "Too many forms in one breath. None could rise." |
| 断网、超时 | `unreachable` | "The spirit could not cross into this world. Try again." |
| 鉴权或额度错误 | `exhausted` | "The ink's power is spent for now." |
| 服务方拒绝内容 | `forbidden` | "This form may not be summoned." |
| Tripo 生成失败 | `collapsed` | "The form collapsed before it could take shape." |
| 回放时作品文件损坏或丢失 | `faded` | "This memory has faded." |

绑骨、套动作、图片精修、声音出错都不算失败,只是悄悄降级。

### 7.2 失败演出(约 3.5 秒)

| 时间 | 画面 | 声音与震动 |
|---|---|---|
| 0.0 秒 | 燃烧进度冻结;燃烧边缘在 1.2 秒内从橙色退到暗红再退到灰色,冒出几缕青烟。 | 火苗熄灭的「嘶」声(预制音效)。 |
| 1.2 秒 | 悬浮光晕和粒子淡出,上下浮动停止,卷轴轻抖一下(0.3 秒)。 | 一声低沉的「泄气」声。 |
| 1.6 秒 | 打开重力,卷轴掉落到莲花台地面。 | 落地的纸木闷响;玩家手里拿着时手柄震一下。 |
| 1.8 秒 | 落点上方淡入失败提示语(英文,世界空间文字,墨黑色衬线字体),停留 3 秒后淡出。 | — |

这段演出用的音效是预先做好的素材,不是每次现场生成。

### 7.3 残卷(D14)

- 失败的卷轴变成物理物体:非运动学 `Rigidbody`,用整张画布大小的抓取碰撞体,可以捡、可以扔(`throwOnDetach` 打开,不回原处)。
- 约 12 秒后碎成灰(溶解效果加少量灰烬粒子)并销毁。
- 玩家继续用桌子后方的备用卷轴重新画(沿用现有 `ScrollStation` 流程)。

### 7.4 状态机改动

- `ScrollRitual`:新增 `Burning -> Failed`。进入 `Failed` 时:停止燃烧、释放悬停区、切换成物理物体、播放失败演出。
- `ScrollStation` 不用改:正在烧或已失败的卷轴已经离开桌面,可以放新卷轴。
- 文字用 TextMeshPro,只需要一个拉丁衬线字体。

---

## 8. 作品库与回放(持久化)

每次成功生成一个物体或场景(splat),就把它完整存到本地,形成「作品库」。作品库里的每一项在游戏里表现为抽屉中的一卷画,可以拿出来重新「烧」一次,把当时的物体或场景原样召唤回来,不再调用任何 API。

> 「物」和「境」都用这一节。「物」随阶段 3/5 实现;「境」的写入和载入随阶段 6 一起做,这里先把格式定好。

### 8.1 存什么

| 内容 | 物 | 境 | 说明 |
|---|---|---|---|
| 墨迹层 `ink.png` | 有 | 有 | 画布原分辨率(2048×1024),回放时原样还原到画布 |
| 印章层 `seal.png` | 有 | 有 | 同上,带透明通道 |
| 模型 `model.glb` | 有 | — | 最终版本:带骨骼动画的,或静态的 |
| 声音 `sound.mp3` | 有就存 | — | 声音比模型晚到时补写 |
| 场景 `world.spz` | — | 有 | splat 文件 |
| 场景碰撞体 `collider.glb` | — | 有 | |
| 精修参考图 `refined.png` | 有就存 | — | 方便以后查看,不参与回放 |
| 清单 `entry.json` | 有 | 有 | 见 8.2 |

失败的作品不存。

### 8.2 清单 `entry.json`

```json
{
  "schemaVersion": 1,
  "id": "2026-10-01T14-32-05_7f3a",
  "createdAt": "2026-10-01T14:32:05Z",
  "seal": "Object",
  "subject": "a galloping horse",
  "category": "quadruped",
  "files": { "ink": "ink.png", "seal": "seal.png", "model": "model.glb", "sound": "sound.mp3" },
  "object": {
    "animated": true,
    "animations": ["preset:quadruped:walk"],
    "sizeM": 0.6,
    "sound": { "kind": "oneshot", "trigger": "on_grab" }
  },
  "world": null,
  "source": { "tripoModel": "P1-20260311", "rigModel": "v2.5-20260210", "tripoTaskId": "..." }
}
```

「境」的 `world` 段记录 splat 的坐标约定(RDF)、对齐参数(位置、旋转、缩放、地面高度)和 World Labs 返回的语义信息。

### 8.3 存在哪里、怎么写

- 位置:`Application.persistentDataPath/Library/<id>/`,外加索引文件 `Library/index.json`(作品 id、所属印章、创建时间)。
- 盖章时就把 `ink.png`、`seal.png` 写进临时目录 `Library/_pending/<jobId>/`。
- 生成成功、所有必需文件都下载完之后,把临时目录**整体改名**成正式目录,再更新索引。中途崩溃或断电不会留下半个作品(D21)。
- 生成失败时删掉临时目录。
- 声音比模型晚到时,补写 `sound.mp3` 并更新 `entry.json`。
- 读取时先校验 `schemaVersion` 和文件是否齐全;不齐全的作品不放进抽屉,只写一条警告日志。

### 8.4 抽屉里的卷轴

- 左抽屉放「物」,右抽屉放「境」(D17)。
- 抽屉内部约 46×51×6.7 厘米,卷起的卷轴约 41×7×3.4 厘米,单层并排每个抽屉放 6 卷。
- 每个抽屉只显示**最新的 6 卷**;更早的作品仍在硬盘上,只是不在抽屉里出现(以后可以加翻页或书架)。
- 场景启动时按索引在抽屉里生成卷轴;新作品生成成功后,立刻在对应抽屉里多出一卷,配一声轻响。
- 卷轴放在抽屉底板上,是普通的可抓取物体,随抽屉一起滑动(抽屉已有真实碰撞体)。
- 区分标记(不用文字):卷轴系一根丝带,「物」红色、「境」青色;卷轴一端挂一张小纸签,纸签上是这卷画的印章印记。
  - 可选:纸签背面印 GPT 给出的英文主体名(如 "Horse")。这是游戏内文字,用英文。

### 8.5 回放流程

1. 玩家拉开抽屉,拿出一卷。
2. 放到画画位上:和备用卷轴规则一样,需要画画位空着(上一张已经飘走);卷轴滑到桌面中心,播放展开动画。
3. 展开后就是当时那幅画,带着当时盖的印。**锁定:不能画、不能再盖印**(D18)。
4. 展开完成约 1 秒后**自动浮空**,按悬停避让规则飘到玩家面前,不需要盖章。
5. 玩家用烛火点燃后,播放**固定 10 秒**的燃烧动画,不调用任何 API,也没有等待点(D19)。
6. 一点燃就开始从硬盘载入模型或 splat;10 秒烧完时:
   - 「物」:在悬停位置化形,动画、声音、尺寸都和当初一样;
   - 「境」:和第一次一样进入那个世界。
7. 如果 10 秒烧完时还没载入好(大的 splat 可能要几秒),火焰停在最后的余烬状态,载入完成再化形。
8. 文件损坏或丢失:走失败演出(第 7 节),原因代码 `faded`。
9. 回放不消耗作品:烧完后,这卷画重新出现在抽屉原来的位置(D20)。

### 8.6 状态机与代码改动

- `ScrollRitual` 新增「回放」模式:由作品库条目创建,开场为卷起状态;展开后跳过作画阶段,直接进入 `Levitating`;`CanSeal` 恒为 false,画布一直锁定。
- `InkCanvas` 新增 `LoadLayers(ink, seal)`:把两张 PNG 画进墨迹层和印章层。
- 燃烧组件(阶段 4)支持两种节奏:生成用的「等结果 + 40% 等待点」,回放用的「固定 10 秒 + 末尾余烬等待」。
- `ScrollStation` 不用改,回放卷轴走同一套「放下 → 滑到中心 → 展开」的流程。

### 8.7 容量

- 一个「物」作品约 2~20 MB(取决于贴图分辨率和面数,P1 更小)。
- 一个「境」作品主要是 splat,可能几十到上百 MB,具体等阶段 6 实测。
- 配置项 `library.maxDiskMB` 给总容量设上限,超出时只提示,不自动删除玩家的作品。

### 8.8 和兜底内容的关系(加分项)

TechPlan 第 9 节的兜底方案需要「内置内容」,作品库格式可以直接复用:把几份精选作品放进 `StreamingAssets/Library/` 随包发布,首次启动时抽屉里就已经有卷轴。评委不配 API key 也能体验「烧卷轴 → 化形 / 进入世界」的完整仪式。

---

## 9. 数据结构(C#)

```csharp
enum JobVerdict { Pending, Ok, Fail }
enum FailReason { Unrecognizable, TooAbstract, NearlyBlank, Crowded, Unreachable, Exhausted, Forbidden, Collapsed }

class VisionPlan      // 从 GPT 返回解析
{
    string status, reason, subject, seen, category, refinePrompt, modelPrompt;
    AnimationPlan animate; SoundPlan sound; float sizeM;
}
class AnimationPlan { bool wanted; string rigType; string[] animations; }
class SoundPlan     { bool wanted; string prompt; string kind; string trigger; float durationS; }

class ObjectJob       // 每张盖过章的卷轴一个;驱动燃烧和化形
{
    JobVerdict Verdict; FailReason? Reason;
    VisionPlan Plan;
    string GlbPath; bool Animated; AudioClip Sound;
    float Progress;   // 过了等待点之后驱动 _BurnProgress
    event Action<JobVerdict> VerdictReady;
    event Action Completed;
}

class LibraryEntry    // 对应 entry.json(第 8.2 节)
{
    int schemaVersion; string id, createdAt, seal, subject, category;
    Dictionary<string, string> files;   // ink, seal, model, sound, world, collider, refined
    ObjectInfo obj; WorldInfo world; SourceInfo source;
    string Folder;                      // 运行时填入,不写进文件
}
```

---

## 10. 配置(`maliang.config.json`,已在 .gitignore 中)

| 配置段 | 字段 | 说明 |
|---|---|---|
| `vision` | `provider` = `openai`、`apiKey`、`model` | `model` 按 `/v1/models` 的结果填写(D10) |
| `imageGen` | `enabled`、`model` = `gpt-image-2.5-sunburst` | 用 `vision` 的 key |
| `tripo` | `apiKey`、`creatureModel` = `P1-20260311`、`staticModel` = `v3.1-20260211` | |
| `sound`(新增) | `provider` = `elevenlabs`、`apiKey`、`enabled` | key 尚未提供 |
| `limits` | `maxGenerationsPerSession`、`objectTimeoutSec` | 已有 |
| `library`(新增) | `enabled`、`perDrawer` = 6、`maxDiskMB` | 作品库(第 8 节) |

key 不会出现在日志里(`MaliangLog` 会遮蔽),也不会提交。打包版本把 key 放在本地配置文件里;如果公开发布,应改成通过服务器中转调用。

---

## 11. 代码结构(`Assets/Maliang/` 下)

| 文件 | 职责 |
|---|---|
| `Http.cs` | 通用请求:超时、有限次指数退避重试、把错误归类到 `FailReason`。 |
| `VisionClient.cs` | 调 GPT,系统提示词放在 `StreamingAssets/Prompts/vision_object.txt`,用严格 JSON 格式;解析成 `VisionPlan`。 |
| `ImageRefineClient.cs` | OpenAI 图片编辑。 |
| `TripoClient.cs` | 上传、建模、轮询、下载;预检、绑骨、套动作。 |
| `SoundClient.cs` | ElevenLabs 音效生成,返回 `AudioClip`。 |
| `ObjectAgent.cs` | 编排一个 `ObjectJob`:各步骤、并行的声音、绑骨把关、结果判定和失败归类。 |
| `Ritual/ScrollFailure.cs` | 失败演出和残卷行为(第 7 节)。 |
| `Loading/ObjectSpawner.cs` | 载入 GLB、统一尺寸、可抓取、骨骼动画或 `ProceduralMotion`、声音触发。 |
| `Library/LibraryStore.cs` | 作品库读写:临时目录、整体改名提交、索引、校验、补写声音。 |
| `Library/LibraryDrawer.cs` | 按索引在左右抽屉里生成回放卷轴(丝带颜色、印章纸签),新作品到来时补一卷,回放后放回原位。 |
| `Drawing/InkCanvas.cs`(改) | 新增 `LoadLayers(ink, seal)`。 |
| `Ritual/ScrollRitual.cs`(改) | 新增回放模式:展开后自动浮空、锁定作画和盖印、固定 10 秒燃烧后载入本地内容。 |
| 编辑器测试入口 | 菜单项:选一张 PNG,不进头显直接跑整条管线,把规划、图片、GLB 和音频都写到 `TestData/Agent/`。 |

---

## 12. 耗时与成本(每个物体)

| 步骤 | 典型耗时 | 成本 |
|---|---|---|
| GPT 看图(`gpt-6.1-sol`) | 实测 10~15 秒 | 很少(约 4800 输入 token + 350 输出) |
| 图片精修(sunburst,1024²,high) | 实测 35~40 秒 | 按张计费 |
| Tripo 建模 | 实测 P1 约 60~100 秒 / 50 积分;v3.1 默认参数 154 秒 / 30 积分(148 万面,需限面数) | 见左 |
| 预检 | 实测 5 秒 | 免费 |
| 绑骨 | 实测 5~13 秒 | 25 积分 |
| 套动作 | 实测 5~9 秒 | 每个动作 10 积分 |
| 声音(并行) | 实测 1.5 秒(3 秒音效) | 一次 ElevenLabs 生成 |
| **合计** | **实测约 2~3 分钟** | **一个会动的生物:四足 85 积分,人形两个动作 95 积分** |

燃烧(阶段 4)要盖住这段等待。表中数据为 2026-10-01 实测(马、道士各一次)。

---

## 13. 正式实现前要先做的实测

1. **glTFast 载入带动画的 GLB**:运行时载入一个绑过骨、套过动作的 Tripo GLB,并播放烘焙在里面的动画。**通过**:马(26 根骨骼)、道士(41 根骨骼)都能载入并播放(Legacy 动画)。
2. **P1 质量**:同一张精修图分别用 `P1-20260311` 和 `v3.1-20260211` 生成,比较外形和贴图质量、面数和 VR 帧耗。如果 P1 质量明显不够,全部改用 v3.1。(模型不需要水墨风格。)**结论**:P1 4581 面、外形和比例正确、细节较简化;v3.1 默认 148 万面、细节明显更好但 VR 带不动。维持 D5:生物用 P1,静态物体用 v3.1 并限面数。
3. **图片精修**:5 张测试画过一遍 `gpt-image-2.5-sunburst`,检查主体和颜色特征是否保留、姿势要求是否遵守(精修图不需要水墨风格)。**已测 2 张**:主体和颜色保留;第一版提示词下长袍遮腿加拄杖的人物预检不能绑骨,加上「露出双腿、双手空着、A 姿势」后同一张画能绑骨。
4. **识别尺度**:20 张测试画(清楚的 10 张、写意松散的 5 张、乱涂的 5 张)。目标:清楚的和写意的都不失败,乱涂的全部失败。**结果**:清楚和写意 15 张全部成功、类别正确;乱涂 5 张中 3 张失败,2 张被宽松解读(锯齿线读成山,合理;分散的几团线读成帆船,偏牵强)。测试画是合成的,偏工整,之后用 VR 里的真实画补测。
5. **运行时 MP3**:在 Windows 打包版本里解码 ElevenLabs 的 MP3。(ElevenLabs 生成已通过;解码放到 `SoundClient` 实现时测。)
6. **作品库往返**:存一个带动画和声音的物体,重启游戏后从抽屉回放,检查画面、印章、动画、声音、尺寸是否完全一致,并记录载入耗时。

---

## 14. 验收标准

- 画清楚的人、马、鱼,各自生成会动的物体;画鹤生成带程序飞行的静态模型;画灯笼生成静态物体;至少有一个生成了合适的声音。
- 乱涂的画触发失败演出并显示对应的英文提示语;卷轴落地、能扔、最后化成灰;备用卷轴流程照常。
- 人为制造断网,走同一套演出并显示 `unreachable` 的提示语。
- 游戏内没有任何非英文的文字,打包版本里没有中文字体。
- 成功生成后,对应抽屉里立刻多出一卷;重启游戏后仍在。
- 回放卷轴展开后是原画和原印章,不能画也不能盖印,展开后自动浮空;点燃后烧 10 秒,化形结果和当初一致;烧完后卷轴回到抽屉。
- 删掉某个作品的模型文件后回放,走失败演出并显示 `faded` 的提示语。

---

## 15. 待定事项

- ElevenLabs 的 API key(尚未提供)。
- 最终使用的 GPT 文本模型型号(D10)。
- v1.0 预设动作的准确名字(写提示词文件时补)。
- 图片精修的价格和各步骤的真实耗时(第 12 节)。
- 作品的删除方式(目前没有设计)。
- 「境」作品的 splat 体积和 `library.maxDiskMB` 的默认值(阶段 6 实测后定)。

## 资料来源

- Tripo 自动绑骨:https://developers.tripo3d.ai/en/docs/animations-rig
- Tripo 绑骨模型:https://developers.tripo3d.ai/en/models/rig
- Tripo 动画套用:https://developers.tripo3d.ai/en/docs/animations-retarget
- Tripo API 快速开始:https://developers.tripo3d.ai/en/docs/quick-start
- Tripo 预检输出说明:https://docs.comfy.org/built-in-nodes/TripoRigCheckNode
- 自动绑骨评测(2026):https://www.strayspark.studio/blog/ai-auto-rigging-showdown-2026-tripo-meshy-cascadeur-mixamo
- OpenAI 图片生成:https://developers.openai.com/api/docs/guides/image-generation
- ElevenLabs 音效生成:https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert
- ElevenLabs 价格:https://elevenlabs.io/pricing/api
