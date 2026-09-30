---
Type: PlanTodo
project: Maliang 马良
status: 未开工
date: 2026-09-30
related: "TechPlan.md"
---

# Maliang 分阶段执行清单

> 依据 [TechPlan.md](TechPlan.md) 的里程碑 M0~M7 拆成可勾选的任务。每个阶段末尾有**验收门槛**,不通过不进入下一阶段(标注"可并行"的除外)。
> 流程速记:桌面水平画布作画 → 盖章即定稿 → 卷轴自动升空悬浮 → 手持烛火点燃 → 燃烧期间生成 → 卷轴燃尽,物/境出现。
> Unity 工程:`TripoHack-Maliang/My project`(Unity 6000.6.3f1,URP)。新代码放 `Assets/Maliang/`,结构见 TechPlan §3.1。

## 阶段总览

| 阶段 | 对应里程碑 | 目标 | 前置 |
|---|---|---|---|
| 0 | — | 工程与依赖就绪 | — |
| 1 | M0 | **Splat Spike**:头显里看到生成的世界(最大风险先验证) | 0 |
| 2 | M1 | 画 + 印 + 升空 | 0 |
| 3 | M2 | 「物」API 客户端 | 0(可与 1、2 并行) |
| 4 | M3 | 烧:燃烧演出(假进度) | 2 |
| 5 | M4 | 「物」全流程闭环 | 3、4 |
| 6 | M5 | 「境」全流程 | 1、5 |
| 7 | M6 | 兜底、快速模式、桌面模式、打磨 | 5、6 |
| 8 | M7 | 打包与发布 | 7 |

---

## 阶段 0:工程与依赖

- [ ] 安装 XR 相关包:OpenXR、XR Interaction Toolkit、XR Hands(如需要)
- [ ] 项目设置:启用 OpenXR + 目标头显 Interaction Profile;Graphics API 改为 **Vulkan 或 D3D12**(Gsplat 要求);色彩空间 Linear
- [ ] 安装 glTFast(运行时加载 GLB)
- [ ] 引入 `wuyize25/gsplat-unity`(MIT),确认在 Unity 6.6 + URP 下编译通过
- [ ] 建立目录 `Assets/Maliang/{Core,Drawing,Ritual,Api,Loading,Fallback,VR,Desktop,Art}` 与 `StreamingAssets/{Prompts,Fallback}`
- [ ] 从 `ReferenceProject/Assets/Node_Brush` 拷贝要复用的资源到新工程:笔模型 `pen.FBX` + 动画、11 张笔触贴图、`penDraw.shader`、书桌 FBX 与贴图、印章插画
- [ ] 补 `.gitattributes`:把 `.glb`、`.gltf`、`.spz` 加入 LFS 规则
- [ ] 建配置系统骨架:`Config` 读取 exe 旁的 `maliang.config.json`;提交 `maliang.config.example.json`(真实文件已在 `.gitignore`)
- [ ] 日志封装(分级、可写文件),后续 API 错误分类使用
- [ ] 验证 Unity MCP 可用(已连通),约定 Claude 通过 MCP 建场景/脚本的分工

**验收:** 空场景能在头显里进入并看到手柄;工程无编译错误;Vulkan/D3D12 下运行正常。

---

## 阶段 1(M0):Splat Spike —— 最大风险,先做

- [ ] 用 World Labs 网页/API 手动生成一个世界,下载 `full_res` SPZ、`500k` SPZ、碰撞体 GLB、`semantics_metadata`
- [ ] 在 Unity 中 `LoadFromSpz(path)` 加载到 `GsplatRenderer`
- [ ] 头显实测:单通道实例化 / 多通道两种渲染模式,记录是否黑屏、左右眼错位
- [ ] 实测帧率:`full_res`(约 2M 点) vs `500k`,记录显卡型号
- [ ] 坐标转换:试 `SourceCoordinates` 枚举,或对 `WorldRoot` 绕 X 轴 180° 旋转,直到方向正确
- [ ] 应用 `metric_scale_factor` 与 `ground_plane_offset`,使玩家站在地面上、尺度合理
- [ ] 用 glTFast 加载碰撞体 GLB,套同一变换,隐藏渲染仅保留碰撞;验证站立/传送落点
- [ ] 若 SPZ 解码异常,试 SPZ → PLY 走 `LoadFromPlyBytes` 备选
- [ ] 写 Spike 结论(可放 `.ai/` 或本文档末尾):可用的渲染模式、推荐点数档位、变换参数

**验收:** 头显里无黑屏、无双眼错位,帧率达标;站在地面上,碰撞体对齐。

**失败预案:** Gsplat 在头显不可用 → 试备选 GaussPlatUnity;仍不行 → 「境」降级为桌面模式渲染 / 500k 档 / 预生成展示,并回头修订 TechPlan §8。

---

## 阶段 2(M1):画 + 印 + 升空

### 2.1 场景与画布
- [ ] 搭桌面场景:书桌、砚台、笔架、颜料位置、印章位置、烛火位置(桌边)
- [ ] 卷轴模型(先用平面 Quad 占位,美术后换)+ 展开动画
- [ ] 画布:**固定水平平面**;`canvas.InverseTransformPoint(nib.position)` → UV → RT 像素

### 2.2 毛笔(移植 Node_Brush)
- [ ] `PaintingHandler` 去单例,输出改为卷轴材质,**删除撤销栈**,只保留清空
- [ ] Ink RT:不透明白底,深度位数 0,长边按卷轴宽高比取 2048
- [ ] 保留贝塞尔插值、速度变粗细、随机毛边
- [ ] `PenBase` 保留笔尖射线(仅判断是否接触)与深度压感,压感距离取画布局部 y
- [ ] `BonePen` 笔头骨骼动画、`PenColorHandler` 蘸颜料、`PenStyles` 笔触样式
- [ ] XR 抓笔:抓取时调 `Init()`;手柄触觉反馈
- [ ] 卷轴显示 Shader:`Paper × Ink`(正片叠底)再叠 `SealCur`

### 2.3 导出
- [ ] `AsyncGPUReadback` 读 Ink RT
- [ ] 计算墨迹包围盒(加 8~10% 边距)→ 补白边到卷轴宽高比(不拉伸)→ 缩放长边 1536/2048 → 编码 PNG
- [ ] 墨迹面积阈值判定(供盖章检查使用)

### 2.4 印章
- [ ] 实体印章模型 ×2(「物」「境」),底面 4 个角点或约定 Quad 表示印面
- [ ] 印面 4 角点投影到卷轴平面 → 画布 UV → `GL.QUADS` 画入 `SealCur`
- [ ] 触发:印面距离阈值 + 倾斜角 < 约 30~35°;**接触一次触发一次**
- [ ] **盖章前检查墨迹面积**,不足则不落印、不升空,手柄轻震
- [ ] `SealType = 物 | 境` 作为数据保存
- [ ] 盖章生效后:禁用画笔与印章输入 → 印章停留 0.5~1 秒 → 卷轴自动升空到玩家面前手可及处,朝向玩家,轻微悬浮晃动
- [ ] 手柄震动反馈

**验收:** 导出图无印章、无纸纹、比例正确;盖章姿态与实体一致;空卷轴盖章不生效;盖章后卷轴稳定升空。

---

## 阶段 3(M2):「物」API 客户端(可与阶段 1、2 并行)

- [ ] `Http` 基础层:超时、指数退避重试(有限次)、错误分类(网络/鉴权/额度/内容/未知)
- [ ] `GenerationJob` 统一状态机:`Kind`、`Stage`、`Progress`、`Result`、`Error`、可取消
- [ ] `VisionClient`:画作 PNG → VLM → 物体描述与 Object Prompt(provider 抽象,提示词放 `StreamingAssets/Prompts/vision_object.txt`)
- [ ] `ImageGenClient`(可选):生成干净背景参考图,失败则跳过
- [ ] `TripoClient`:上传 → 建任务 → 轮询 → **立即下载** GLB(链接有效期短);**先核对官方文档,TechPlan 中端点细节尚未核实**
- [ ] 编辑器脚本或命令行入口,便于不进头显直接测试:一张画 → 一个 GLB
- [ ] `GlbLoader`:glTFast 运行时加载 + 尺度归一化 + 碰撞体 + `XRGrabInteractable`
- [ ] 会话内生成次数限额

**验收:** 一张画得到可加载、可抓取的 GLB;失败有重试与分类;超时可控。

---

## 阶段 4(M3):烧

- [ ] Burn / Dissolve Shader:采样合成结果(纸 × 墨 + 印),`_BurnProgress` 控制;**燃烧起点取火焰接触点的卷轴 UV**
- [ ] 火焰粒子、灰烬、火星、环境光随进度增强
- [ ] 可抓取烛火:烛台/蜡烛模型 + 火焰触发体;放下后回到桌边
- [ ] 点燃判定:卷轴处于 `Levitating` + 火焰持续接触约 0.3~0.5 秒
- [ ] 卷轴状态机落地:`Rolled → Unrolled → Levitating → Burning → Materializing → Done/Failed`
- [ ] 虚拟进度器:单调不减;按 TechPlan §7.2 分阶段预算;主生成阶段渐近曲线;真实完成后快进
- [ ] 「境」用里程碑事件(火势分批加大、卷轴分批脱落、环境音变化)
- [ ] 先用**假计时器**驱动完整燃烧演出

**验收:** 视觉上进度与阶段吻合;无论假进度提前或延迟完成,演出都不突兀。

---

## 阶段 5(M4):「物」全流程闭环

- [ ] 点燃 → `GenerationJob` 启动 → 导出 Ink PNG → Vision → (参考图) → Tripo → 下载 GLB
- [ ] `GenerationJob` 进度驱动 `_BurnProgress`
- [ ] 燃尽处化形:GLB 出现在卷轴悬浮位置,可伸手抓取
- [ ] 失败路径:重试 → 仍失败则进入兜底(阶段 7 前先用一个内置模型顶替)
- [ ] 全流程手测 10 次,记录耗时与失败率
- [ ] (加分项)Tripo 多视角参考图,不阻塞首版

**验收:** 画 → 盖「物」印 → 点燃 → 等待 → 拿到自己画的东西,全流程打通。

---

## 阶段 6(M5):「境」全流程

- [ ] `WorldLabsClient`:`prepare_upload` → 签名 URL 上传 → `worlds:generate` → 轮询 operation → 取 world 资源与语义元数据(**以官方文档核对字段**)
- [ ] `vision_world.txt` 提示词:环境、空间结构、建筑、地貌、天气、时间、颜色、氛围 → Environment Prompt
- [ ] 下载 SPZ + 碰撞体 GLB,**立即落盘**到 `persistentDataPath`
- [ ] `SplatWorldLoader` + `WorldAligner`:套阶段 1 得到的变换参数
- [ ] 进入世界:燃尽 → 桌面场景淡出/溶解 → 玩家出现在世界地面 → 传送移动
- [ ] 设计并实现**退出方式**(TechPlan §14 待设计)
- [ ] 进入世界时隐藏/卸载桌面房间,避免叠加
- [ ] 点数档位可配置(`full_res` / `500k`)
- [ ] 验证碰撞体是否含在生成结果内、账单与计费

**验收:** 玩家可站在生成的世界里移动,并能回到桌面。

---

## 阶段 7(M6):兜底、快速模式、桌面模式、打磨

- [ ] 预生成 3~6 组资源(剑、龙、山、城堡等)放入 `StreamingAssets/Fallback/` 并写 `meta.json`(关键词、类型、尺度、对齐)
- [ ] `FallbackMatcher`:VLM 关键词匹配 → 退回同类型随机
- [ ] 触发降级:API 失败 / 额度耗尽 / 断网 / 无密钥 / 超时 / 限额;演出保持一致
- [ ] 快速体验模式:「境」直接用预生成结果 + 缩短燃烧
- [ ] 桌面模式:鼠标绘制(参考 `Pen_2d`)、键盘交互,复用同一条演出
- [ ] 错误只写日志与降级决策,玩家看到的是仪式演出,不弹报错
- [ ] 音效:翻卷轴、蘸墨、盖章、火焰、境的环境音
- [ ] 手感调参:印章倾斜阈值、升空高度/朝向/时长、点火接触时间、燃烧节奏
- [ ] 美术打磨:卷轴模型与展开动画(可用 Blender MCP)、纸纹、环境氛围

**验收:** 断网、无密钥条件下 demo 仍能完整体验;桌面模式可用。

---

## 阶段 8(M7):打包与发布

- [ ] 出 Windows 独立包,`maliang.config.json` 放 exe 旁
- [ ] 为 demo 专门开密钥;设额度与硬性花费上限;会话限额已生效
- [ ] 确认各服务条款对随包分发密钥的规定
- [ ] 确定分发渠道(私有链接/压缩包,避免公开页面导致密钥暴露)
- [ ] 录屏作为最后保障
- [ ] 写说明文档:运行要求(Vulkan/D3D12、头显)、配置方法、桌面模式、快速模式
- [ ] 评审结束后立刻作废/轮换密钥
- [ ] 在干净机器上做一次「下载即玩」验证

**验收:** 按 TechPlan §10 检查清单全部通过。

---

## 持续事项

- [ ] 每个阶段结束在 `.ai/` 或本文档记录实测数据与决策(尤其是阶段 1 的头显结果)
- [ ] 每完成一个阶段提交一次,并及时回写 TechPlan 中已被证伪或已变化的假设
- [ ] 待确认项(TechPlan §14):兜底内容清单、VLM 服务选型、评委是否有头显、发布渠道、是否首版做 Tripo 多视角
