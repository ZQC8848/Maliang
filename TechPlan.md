---
Type: TechPlan
project: Maliang 马良
status: 路线已敲定,尚未开工
date: 2026-09-29
related: "Idea：Maliang 马良（VR 生成式创作仪式：画→印→烧→化形）.md"
---

# Maliang 技术方案(TechPlan)

> 体验概要:**画 → 印 → 烧 → 化形**。玩家在 VR 里用毛笔在桌面卷轴上作画,用「物」或「境」印章决定意图并定稿,卷轴随即自动飘到半空悬浮,玩家手持烛火将其点燃,AI 管线在燃烧期间运行,卷轴燃尽后画作成为现实(可抓取的 3D 物体,或可进入的 3D 世界)。
> 本文档记录**已经敲定的技术路线**、模块设计、里程碑与待验证事项。体验设计见 Idea 文档。

---

## 0. 已敲定的决策一览

| 项 | 决定 | 备注 |
|---|---|---|
| 引擎 | **Unity 6 + URP + OpenXR** | 新建工程,不沿用参考项目的 Unity 2020.3 |
| 目标设备 | **PC VR 串流**(Quest Link / SteamVR) | 算力足够渲染 2M 点的高斯泼溅 |
| 图形 API | **Vulkan 或 D3D12**(项目级设置) | Gsplat 的排序依赖 wave 操作,其他 API 不能用 |
| 「境」渲染 | **3D Gaussian Splat**,使用 `wuyize25/gsplat-unity`(MIT) | 运行时 `LoadFromSpz(path)`;待头显实测 |
| 「物」加载 | **glTFast** 运行时加载 GLB | |
| API 架构 | **无独立后端**,全部在 Unity C# 内直连 | 评委下载即玩,密钥随包分发 |
| 项目范围 | **完整产品原型**:两条管线都真实可用,含错误处理与重试 | |
| 印章 | 单枚,**盖章即定稿**(盖章后卷轴自动升空,不再作画,无替换);印面姿态即盖章姿态;墨迹层与印章层分离 | 见 §5 |
| 画布与点火 | 作画时画布为固定在桌面的水平平面;盖章后自动飘到半空悬浮;**玩家手持烛火**点燃悬浮卷轴 | 见 §4、§6 |
| 导出 | 只导出墨迹层;保持卷轴比例;裁掉空白 | 见 §5.5 |
| 降级 | 预生成结果放入包内,任何失败均可无缝兜底 | 见 §9 |

---

## 1. 参考项目(Node_Brush)评估

**位置:** `ReferenceProject/`。Unity 2020.3.38,项目名 MarkPen,原本面向 PICO。**只覆盖"画"的一部分**,是桌面毛笔书写演示。

### 1.1 有什么

| 模块 | 文件 | 说明 |
|---|---|---|
| 毛笔绘制核心 | `Scripts/Pen_3d/PaintingHandler.cs` | 用 `GL.QUADS` 把笔触贴图逐个画进 RenderTexture;三阶贝塞尔插值、按速度变粗细、随机毛边、撤销栈(本项目不要)、清空 |
| 笔与压感 | `PenBase.cs` | 笔尖沿 forward 做射线检测;**离画布越近笔越粗**(用 `exp(-x)` 后线性映射) |
| 笔头骨骼 | `BonePen.cs` + `pen.FBX` + Up/Down/Left/Right 动画 | 笔毫随运动方向弯曲 |
| 蘸墨 | `PenColorHandler.cs` | 笔尖进入触发器(Tag=`Finish`)即换色 |
| 笔触样式 | `PenStyles.cs` + 11 张笔触贴图 | |
| 印章 | `Seal.cs` | 继承 `PenBase`,把印章贴图当作笔触画进**同一张画布** |
| 绘制 Shader | `Shaders/penDraw.shader` | 预乘 alpha 混合,按颜色亮度选择反相逻辑 |
| 美术 | 中式书桌 FBX(约 7.9MB,含砚台、笔架)、印章插画、笔模型 | |
| 2D 版本 | `Pen_2d/` | 鼠标绘制,可作桌面模式(§9.4)的起点 |

### 1.2 缺什么

- **没有 VR 交互**:没有 XR Interaction Toolkit,没有手柄输入与抓取。`manifest.json` 只有旧版 `modules.xr/vr`;`EditorBuildSettings` 里残留 PICO 配置引用,但 PICO SDK 包不在工程中。
- 没有网络与 API 代码。
- 没有卷轴、烛火、燃烧 Shader、粒子。
- `PaintingHandler` 是**单例 + UI `RawImage` 画布**,没有多层画布,没有导出接口。
- `Seal` 只是往同一张画布贴图,且**拖动会连续绘制**;不记录印章类型。
- 画布坐标依赖 `Canvas3d` 的 `RectTransform` 换算,不适用于卷轴网格。
- 参考项目里 `ClearRender()` 用的是零面积四边形,实际不起作用,不要照搬。

---

## 2. 复用与自研对照

### 2.1 直接复用(小改)
- 笔触渲染:贝塞尔插值、速度变粗细、毛边(`PaintingHandler` 内的绘制部分)
- 深度压感(`PenBase.ChangeBurshSize` 及其映射函数)
- 笔头骨骼动画(`BonePen` + 动画控制器 + `pen.FBX`)
- 颜料触发器(`PenColorHandler`)、笔触样式(`PenStyles`)、11 张笔触贴图
- 书桌 FBX、砚台、笔架、`penDraw.shader`、印章插画(作为「物」「境」两章的美术参考)

### 2.2 复用但要改
| 项 | 改动 |
|---|---|
| `PaintingHandler` | 去单例;画布从 UI `RawImage` 改为卷轴网格材质;**拆成多层(Ink / SealCur)**;新增导出;**删除撤销栈**(不做撤销功能),仅保留清空 |
| `PenBase` | 抓取时调用 `Init()`(源码注释里已规划);保留笔尖射线与压感(画布固定水平,原有"笔尖到画布的垂直距离"逻辑可直接沿用) |
| `Seal` | **重写**为「接触一次盖一次」,不再继承画笔的连续绘制逻辑(见 §5) |

### 2.3 必须自研
1. VR 骨架:XR Interaction Toolkit、手柄抓取、桌面场景
2. 卷轴:模型、展开/收起动画(可用 Blender MCP 辅助)、水平平面画布映射
3. 印章:实体模型、印面投影、单章逻辑(盖章即定稿,触发卷轴升空)
4. 烧:Burn/Dissolve Shader(从烛火接触点开始蔓延)、火焰粒子、灰烬、可抓取烛火 + 火焰接触点燃触发、盖章后卷轴自动升空悬浮
5. 生成管线:导出 → VLM → 参考图 → Tripo / World Labs → 下载 → 运行时加载
6. 进度到燃烧阶段的映射(§7)
7. 化形:物体出现(可抓取)、世界出现(可进入)
8. 降级路径、配置系统、桌面模式

---

## 3. 总体架构

```
┌────────────────────────── Unity 6 (Windows 独立包, URP, OpenXR) ──────────────────────────┐
│                                                                                            │
│  VR 交互层        XR Rig / 手柄抓取 / 毛笔 / 颜料 / 实体印章 / 手持烛火                    │
│  画布层           PaintingHandler(多层 RT) → 卷轴显示 Shader(纸×墨 + 印)                 │
│  仪式层           ScrollState 状态机 → Burn Shader / 粒子 / 悬浮                            │
│  生成层           GenerationJob(统一状态机) → 进度/阶段 → 仪式层                          │
│    ├ VisionClient       画作 PNG → VLM → 描述 + prompt                                    │
│    ├ ImageGenClient     生成干净背景参考图(可选,失败则跳过)                              │
│    ├ TripoClient        「物」:上传→任务→轮询→下载 GLB                                   │
│    └ WorldLabsClient    「境」:上传→generate→轮询→下载 SPZ + 碰撞体 GLB + 元数据          │
│  加载层           glTFast(GLB) / Gsplat(SPZ) / 碰撞体 / 尺度与地面对齐                     │
│  兜底层           Fallback 资源库(预生成)+ 降级策略                                       │
│  配置层           maliang.config.json(密钥、开关、限额)                                   │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

**为什么无后端仍可行:** 三家 API 都是标准 HTTPS REST,Windows 独立包没有 CORS 限制。
**放弃后端的代价:** 密钥随包分发(§10);提示词改动需可热更新 → 放在 `StreamingAssets/Prompts/*.txt`,打包后仍可编辑。

### 3.1 建议的工程目录

```
Assets/
  Maliang/
    Core/            GenerationJob、ScrollState、Config、Logging
    Drawing/         PaintingHandler(多层)、Brush、SealStamp、Export
    Ritual/          BurnController、CandleTrigger、Levitate、Materialize
    Api/             VisionClient、ImageGenClient、TripoClient、WorldLabsClient、Http
    Loading/         GlbLoader、SplatWorldLoader、WorldAligner
    Fallback/        FallbackLibrary、FallbackMatcher
    VR/              XR rig、Grab、Locomotion
    Desktop/         桌面模式(可选)
    Art/             Models、Materials、Shaders、VFX、Textures
  StreamingAssets/
    Prompts/         vision_object.txt、vision_world.txt、image_gen.txt ...
    Fallback/        objects/*.glb、worlds/*.spz + meta.json
    maliang.config.example.json
```

---

## 4. 画布与绘制(画)

### 4.1 多层画布

```
Ink RT      墨迹层    不透明白底,毛笔画在这里(与参考项目一致)
SealCur RT  印章层    透明底 (0,0,0,0);只盖一次,盖章即定稿
Paper       纸张贴图  属于卷轴材质,不在任何 RT 里
```

- 卷轴显示 Shader:`Paper × Ink(正片叠底)`,再叠 `SealCur`。
- **烧的 Shader 采样合成结果**,所以燃烧时印章与画一起烧掉。
- **导出只读 Ink RT**,因此导出图无印章、无纸纹。
- Ink RT 分辨率按卷轴宽高比选择(例如长边 2048)。

### 4.2 移植改动要点
1. `PaintingHandler` 去单例;根据"当前笔类型"选择目标 RT。
2. **画布是固定在桌面上的水平平面**(作画期间不移动、不倾斜)。坐标不再走 `Canvas3d.RectTransform`,改为:`canvas.InverseTransformPoint(nib.position)` 得到画布局部坐标 → 除以画布尺寸得 UV → 换算为 RT 像素坐标。局部坐标的 y 就是笔尖离画布的垂直距离,直接用于笔压。射线检测仅用于"是否接触画布"的判断,不再需要 `textureCoord` 与 MeshCollider 的 UV。
3. 笔压:保留"笔尖到画布距离 → 粗细"的逻辑;水平固定画布下,参考项目原逻辑成立。
4. **不做撤销**:删除 `cacheList` / `SaveTexture` / `BackDraw`,只保留清空。Ink RT 不需要深度缓冲(深度位数设为 0),以节省显存。
5. XR:玩家抓取笔时调用 `PenBase.Init()`,松手不需要额外调用(源码已按此设计)。

---

## 5. 印章系统(印)

### 5.1 规则
- **一张卷轴只有一枚印章,盖章即定稿**:盖章生效后卷轴立刻升空,不再接受作画与盖章,因此**没有替换与渐隐机制**。
- 位置不限,**印章中心须落在卷轴范围内**(超出边缘的部分被 RT 自然裁掉)。
- 印章图案的**方向、大小、位置与盖下瞬间的实体印面完全一致**。
- 盖章后**不能**再作画(卷轴已升空)。
- **没有盖章则卷轴不会升空,也就无法点燃**(§6.2)。
- 盖章生效前先检查墨迹面积(§5.5 第 5 点);面积不足时印章不落印、不升空,只给手柄一次轻震。
- `SealType = 物 | 境` 是**数据**,由代码直接决定走 Tripo 还是 World Labs,**不需要 AI 看见印章**。

### 5.2 印面投影(核心实现)
1. 在场景里制作实体印章,印章底面放 4 个标记点,或一个约定好的底面 Quad,表示"印面"。
2. 触发盖章的那一帧,把 4 个角点的世界坐标投影到卷轴平面,换算为画布 UV。
3. 用 `GL.QUADS` 把印章图案贴到 `SealCur`,四个顶点即这 4 个 UV 点。参考项目本来就是这样画笔触的,区别只是它画的是水平矩形,这里是任意四边形。位置、旋转、大小自然与实体一致。

### 5.3 触发条件
- 印面距离卷轴平面小于阈值,即触发**一次**盖章(盖章即定稿,无需再做解锁逻辑)。
- **倾斜限制:** 印面与卷轴平面夹角小于约 30~35° 才盖章,否则不盖(或盖出较淡的残缺章)。此阈值后续调参。
- 印章无压感、无毛边、无贝塞尔插值。
- 触觉反馈:盖章瞬间给手柄一次震动。

### 5.4 盖章后升空
1. 盖章生效(§5.3)→ 记录 `SealType`,把印章画进 `SealCur`,卷轴状态转 `Levitating`(§6.1)。
2. 印章先在桌面上停留约 0.5~1 秒,让玩家看清印记,再开始升空。
3. 升空为**自动动画**:卷轴从桌面固定位置缓动到玩家面前的半空,朝向玩家,取消与桌面的交互;高度需在手可及的范围内(便于伸手点火)。位置、朝向、缓动曲线与悬浮微晃动为手感参数,后续调。
4. 升空开始后,画笔与印章的输入全部禁用。

### 5.5 导出(送往 VLM 与生成管线)
1. 从 Ink RT 用 `AsyncGPUReadback` 异步回读。
2. 计算有墨迹区域的包围盒,加约 8~10% 边距。
3. 把该区域**扩展成与卷轴相同的宽高比**,以墨迹中心为中心补白边,**不拉伸**。
4. 缩放到长边约 1536 或 2048,编码 PNG。
5. 墨迹面积过小时(几乎没画),**盖章不生效**(印章不落印、卷轴不升空),以世界内方式提示(如手柄轻震、印章回弹)。检查发生在盖章那一刻,避免玩家把空卷轴升到半空后无从处理。

> 说明:"保持卷轴比例"与"裁掉空白"存在一点冲突(裁剪会改变比例),这里的处理是"裁剪后再补白边到原比例"。如需改为"紧贴墨迹的矩形、比例不限",只需去掉第 3 步。

---

## 6. 仪式流程(烧)

### 6.1 卷轴状态机

```
Rolled → Unrolled(桌面,可作画/盖章)
      → Levitating(盖章后自动升空并悬浮,等待点火)
      → Burning(被点燃,生成中)
      → Materializing(化形) → Done / Failed→Fallback
```

### 6.2 点燃条件
- 卷轴处于 `Levitating` 状态(即已盖章且墨迹面积达标)。
- 玩家**手持烛火**(桌边一支可抓取的烛台/蜡烛,带火焰触发体),让火焰接触悬浮卷轴,持续接触约 0.3~0.5 秒即点燃。
- 烛火放在桌边固定位置;玩家放手后可回到原位(或留在原地,不影响流程)。
- 卷轴悬浮期间没有超时限制;玩家不点火,卷轴就一直悬在那里。

### 6.3 点燃后的行为
1. 记录火焰接触点(卷轴局部 UV),Burn Shader 以该点为**燃烧起点**向外蔓延,`_BurnProgress` 由 `GenerationJob` 驱动(§7)。
2. 同时**立即**启动生成任务(导出 Ink → VLM → …)。
3. 卷轴始终悬在半空,不需要再做脱手或取消重力。
4. 火焰、灰烬、粒子随进度增强。

### 6.4 化形
- **「物」:** 卷轴燃尽处出现 GLB 模型,套用尺度归一化,添加碰撞体与 `XRGrabInteractable`,玩家可伸手抓取。
- **「境」:** 见 §8。

---

## 7. 燃烧 = 加载条

### 7.1 关键约束
- Tripo 生成通常 **10~120 秒**;World Labs 官方给出 **约 5 分钟**。
- 因此进度不能线性对应一个真实百分比,必须由**虚拟进度**驱动。

### 7.2 虚拟进度设计
- 进度 `p` **单调不减**。
- 按阶段划分预算(下面是初值,实测后调整):

| 阶段 | 「物」占比 | 「境」占比 | 说明 |
|---|---|---|---|
| 导出与上传 | 0–8% | 0–5% | |
| VLM 理解 | 8–20% | 5–12% | |
| 参考图生成(可选) | 20–35% | 12–20% | 失败则跳过 |
| 主生成(Tripo / World Labs) | 35–85% | 20–88% | 时间渐近逼近区间上限,**真实完成前不超过区间上限** |
| 下载与加载 | 85–95% | 88–96% | |
| 化形演出 | 95–100% | 96–100% | 卷轴燃尽,物/世界出现 |

- 「主生成」阶段:进度按"渐近曲线"随时间前进,越接近区间上限越慢,永远不冲破;真实完成事件到来后才跨过上限。
- 「境」时间长,建议**加入里程碑事件**(而不是只有匀速燃烧):例如火势一次次明显加大、卷轴分批脱落、环境音变化,让玩家感到"魔法有进展"。
- 如果生成提前完成,快进燃烧(加速但不突兀);如果超时,见 §9。

---

## 8. 「境」的加载与进入

### 8.1 下载与资源
World Labs 返回:
- `spz_urls`:`100k` / `500k` / `full_res`(约 2M 点)三档 SPZ
- `collider_mesh_url`:碰撞体 GLB(约 10~20 万三角面)
- `semantics_metadata`:`metric_scale_factor`、`ground_plane_offset`
- 另有 `hq_mesh_url`、`full_res_mesh_url`、全景图、缩略图

**下载策略:** 生成完成后立刻下载并落盘到 `persistentDataPath`(文档没有说明 URL 是否过期,不依赖它)。PC VR 默认取 `full_res`,配置里允许降为 `500k`。

### 8.2 加载与对齐
```
UnityWebRequest 下载 → 落盘
 → CreateInstance<GsplatAssetSpz>().LoadFromSpz(path)
 → GsplatRenderer.GsplatAsset = asset
 → 放入 WorldRoot 父物体,设置:
      坐标转换:marble_raw_opencv(+x 左,+y 下,+z 前)→ Unity(实测选 SourceCoordinates 枚举 / 或对 WorldRoot 做旋转)
      整体缩放 = metric_scale_factor(对点位置与点大小同时生效,等价于父物体缩放)
      Y 平移   = ground_plane_offset(使地面落在 y=0)
 → glTFast 加载碰撞体 GLB,套用**相同变换**,隐藏渲染,只保留碰撞
```
- `LoadFromSpz` 只接受文件路径,因此需要先落盘。
- 如 SPZ 解码出现问题,备选:把 SPZ 转成 PLY,改走 `LoadFromPlyBytes(byte[])`。
- Marble 网页查看器对生成的 SPZ 使用绕 X 轴 180° 的旋转,可作为 Unity 里试坐标转换的起点。

### 8.3 进入方式(待细化)
- 火焰燃尽 → 桌面场景淡出(或溶解)→ 玩家出现在世界原点的地面上。
- 移动:传送到碰撞体上的点(舒适度好,实现简单)。
- **退出:** 需要一个仪式化的返回方式(例如手心出现一枚小卷轴/印章,或抬手手势)。**此项属待设计。**

### 8.4 已知限制
- Gsplat 要求 **Vulkan 或 D3D12**。
- World Labs 官方 Unity 指南提示:VR 中**单通道实例化**在部分插件下会黑屏,需用多视图模式。Gsplat 的 README 声称 URP 下多通道与单通道实例化都支持,**但无头显实测证据,必须自测**。
- 官方指南称 500k 的 SPZ 在 Quest 3 一体机上约 12fps,2M 会在一体机上崩溃;PC VR 需按目标显卡实测。
- 「境」渲染时,桌面房间要隐藏或卸载,避免两套场景叠加。

---

## 9. 兜底与降级(评委下载即玩的关键)

### 9.1 触发降级的情况
API 失败 / 额度耗尽 / 网络断开 / 未配置密钥 / 生成超时 / 会话调用次数超限。

### 9.2 降级行为
- **演出保持一致:** 依然播放同样的燃烧演出。
- 由 `FallbackMatcher` 选出最接近的预生成结果,在燃尽时化形出来。评委看到的体验一致。
- 预生成内容:用 3~6 张典型画作(剑、龙、山、城堡等),各生成「物」或「境」结果,放入 `StreamingAssets/Fallback/`,并附 `meta.json`(关键词、类型、尺度、对齐信息)。
- 匹配方式初期可用 VLM 输出的关键词与备用库标签匹配;VLM 也不可用时,退回随机同类型备用结果。

### 9.3 快速体验模式
- 评委不想等 5 分钟时,可选"快速体验":「境」直接使用预生成结果,只播放缩短的燃烧演出。

### 9.4 桌面模式(建议)
- 评委可能没有 VR 头显。建议提供**鼠标+键盘的桌面模式**,复用参考项目的 2D 绘制思路(`Pen_2d`)。
- 同时附一段录屏作为最后保障。

### 9.5 错误处理与重试
- 每个客户端都有:超时、指数退避重试(有限次)、错误分类(网络/鉴权/额度/内容/未知)。
- 失败信息只写日志与降级决策,**玩家看到的是仪式演出,不是报错弹窗**。

---

## 10. 密钥、成本与合规

### 10.1 原则
只要密钥在评委电脑上,就没有真正的保密。策略是**限制损失,而不是隐藏密钥**。

### 10.2 措施
1. **为 demo 专门开密钥**,不使用主密钥。
2. **控制额度:** World Labs、Tripo 是买积分;只充评审期间够用的钱。按月计费的服务(如 OpenAI)设置硬性花费上限。
3. **评审结束后立刻作废或轮换密钥。**
4. **不要提交到 git 或公开仓库**;如果 demo 是公开发布(如 itch.io),密钥等同公开,风险高得多,应优先使用私有链接/压缩包分发。
5. **密钥放在 exe 旁的 `maliang.config.json`**,不编译进程序。密钥失效后只需替换该文件,不需重新打包。随压缩包分发,满足"下载即玩"。
6. **会话内限额:** 每次运行最多 N 次生成,防止连点烧光额度。
7. 发布前确认各家服务条款对"把密钥随 demo 分发"的规定。

### 10.3 成本参考
- World Labs:marble-1.1 单个世界约 1,500 积分(约 $1.20);mesh 导出另计(约 3,500 积分 / $2.80,需确认碰撞体是否包含在生成结果中或需单独导出)。
- Tripo:按任务计费,具体单价开工前查阅账户页面。
- VLM / 图像再生成:按调用计费,需设置上限。

### 10.4 配置文件示例(草案)

```json
{
  "mode": "online",
  "worldLabs":  { "apiKey": "...", "model": "marble-1.1", "splat": "full_res" },
  "tripo":      { "apiKey": "..." },
  "vision":     { "provider": "openai", "apiKey": "...", "model": "..." },
  "imageGen":   { "enabled": true },
  "limits":     { "maxGenerationsPerSession": 6, "worldTimeoutSec": 600, "objectTimeoutSec": 240 },
  "fallback":   { "enabled": true, "fastMode": false }
}
```

---

## 11. 生成管线细节

### 11.1 「物」(Tripo)

```
Ink PNG(已裁剪)
 → VisionClient:识别物体、形状特征、颜色、必须保留的细节 → Object Prompt
 → ImageGenClient(可选):生成干净背景的 Reference Image
      (若 pipeline 允许,进一步生成 Front / Side / Back 多方向参考图,走 Tripo multiview)
 → TripoClient:上传图片 → 建任务 → 轮询 → 下载 GLB(下载链接有效期很短,必须立即下载)
 → glTFast 加载 → 尺度归一化 → 碰撞体 + 抓取
```

- Tripo 典型耗时 10~120 秒;输出 GLB。
- 多视角生成作为**加分项**,不阻塞首个版本。
- 如果参考图生成失败,直接把裁剪后的墨迹图交给 Tripo(降级,不中断)。

### 11.2 「境」(World Labs)

```
Ink PNG(已裁剪)
 → VisionClient:识别环境、空间结构、建筑、地貌、天气、时间、颜色、氛围 → Environment Prompt
 → ImageGenClient(可选):生成适合世界生成的 Reference Image
 → WorldLabsClient:
      POST /marble/v1/media-assets:prepare_upload → 用签名 URL 上传图片
      POST /marble/v1/worlds:generate(图片 + 文本提示)→ 返回 operation_id
      GET  /marble/v1/operations/{operation_id} 轮询直到 done
      GET  /marble/v1/worlds/{world_id} 获取资源与语义元数据
 → 下载 SPZ + 碰撞体 GLB → 落盘 → 加载(§8)
```

- 认证:请求头 `WLT-Api-Key`。
- 大范围室外场景可用 `marble-1.1-plus`(积分更多)。
- 具体接口路径与字段以官方文档为准,实现时逐项核对(本文档中 Tripo 的端点细节尚未核实,开工时以官方文档为准)。

### 11.3 GenerationJob 统一接口(草案)

```
GenerationJob
  Kind        : Object | World
  Stage       : Exporting | Understanding | RefImage | Generating | Downloading | Loading | Done | Failed
  Progress    : 0..1(虚拟进度,单调)
  Result      : GlbPath | SplatWorld(path, meta) | FallbackRef
  Error       : 分类 + 信息
  Cancel()    : 玩家中途放弃的处理(可选)
```

仪式层只订阅 `GenerationJob`,不关心具体 API,这样降级与真实生成用同一条演出。

---

## 12. 里程碑与验收

| 里程碑 | 内容 | 验收标准 |
|---|---|---|
| **M0 骨架 + Splat Spike** | 新建 Unity 6 + URP + OpenXR + Vulkan/D3D12;手动下载一个 World Labs `full_res` SPZ | 头显里能看到世界,无黑屏、无双眼错位,帧率达标;套上尺度与地面偏移后能站在地面上;碰撞体对齐正确 |
| **M1 画 + 印** | XR 抓笔在桌面卷轴上画;多层画布;实体印章一次性盖章、盖章后卷轴自动升空;导出裁剪 PNG | 导出图无印章、无纸纹、比例正确;盖章姿态与实体一致;盖章后卷轴稳定升空到手可及处 |
| **M2 API 客户端「物」** | Unity 内 Vision → Tripo → GLB(可先命令行/编辑器脚本测试) | 一张画得到可加载的 GLB;失败有重试与分类 |
| **M3 烧** | Burn Shader(从接触点蔓延)、火焰、灰烬、手持烛火点燃悬浮卷轴;进度用假计时器驱动 | 视觉上进度与阶段吻合 |
| **M4 「物」闭环** | 盖章 → 点燃 → 真实生成 → 燃烧 → 卷轴燃尽处出现可抓取的 3D 物体 | 全流程打通 |
| **M5 「境」** | World Labs 全链路 → Splat + 碰撞体 → 进入世界 | 玩家可站在生成的世界里移动 |
| **M6 降级与打磨** | 预生成兜底、快速模式、桌面模式、音效、错误演出、配置与限额 | 断网/无密钥下 demo 仍可完整体验 |
| **M7 发布** | 打包、密钥策略、录屏、说明文档 | 按 §10 检查清单通过 |

---

## 13. 风险与未验证项

| 风险 | 严重度 | 说明 / 对策 |
|---|---|---|
| Gsplat 在 Unity 6 + URP + OpenXR 头显中的表现 | **高** | 无实测证据;M0 Spike 首先验证(单通道实例化 / 多通道、黑屏、帧率) |
| SPZ 坐标转换与尺度 | 中 | 需实测 `SourceCoordinates` 选项与父物体旋转 |
| 「境」生成 ~5 分钟,现场体验 | 中 | 里程碑事件 + 快速模式 + 预生成兜底 |
| 评委机器显卡不足以渲染 2M 点 | 中 | 配置降档到 `500k`;桌面模式与录屏兜底 |
| 密钥泄露与额度被滥用 | 中 | §10 措施 |
| Tripo 下载链接有效期很短 | 低 | 拿到即下载;失败重试 |
| World Labs 资源 URL 是否过期未说明 | 低 | 生成完成立即下载并落盘 |
| 碰撞体 GLB 是否含在生成结果内 / 是否要单独导出与计费 | 低 | 实测账单与接口确认 |
| `LoadFromSpz` 只支持路径、SPZ v4(zstd)兼容 | 低 | 已确认包内含 zstd 解码;有 PLY 备选 |
| 服务条款对随包分发密钥的限制 | 低 | 发布前核对 |

---

## 14. 待设计 / 待确认

1. 「境」的**退出方式**(如何回到桌面)。
2. 预生成兜底的**内容清单**(具体哪几张画、哪几个世界)。
3. VLM 服务选型(Idea 文档写 GPT Vision;架构上抽象为 provider,可换)。
4. 评委是否有头显 → 是否投入**桌面模式**。
5. 发布渠道(私有链接 / 压缩包 / 公开页面),决定密钥泄露风险。
6. 印章倾斜角阈值、升空高度/朝向/时长、点火接触时间、燃烧节奏等**手感参数**(实测调整)。
7. 是否在首版实现 Tripo 多视角参考图(加分项)。

---

## 15. 参考资料

- World Labs API:<https://docs.worldlabs.ai/api>
- World Labs SPZ 渲染指南:<https://docs.worldlabs.ai/api/rendering-spz.md>
- World Labs Unity 导出指南:<https://docs.worldlabs.ai/marble/export/gaussian-splat/unity.md>
- World Labs 导出文件规格:<https://docs.worldlabs.ai/marble/export/specs>
- Tripo 开发者文档:<https://developers.tripo3d.ai/en/docs/quick-start>
- Gsplat(Unity 高斯泼溅渲染):<https://github.com/wuyize25/gsplat-unity>
- 备选:GaussPlatUnity:<https://github.com/denisislamov/GaussPlatUnity>
- 排除:aras-p/UnityGaussianSplatting(仅编辑器建资产,无运行时加载):<https://github.com/aras-p/UnityGaussianSplatting>
