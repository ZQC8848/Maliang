---
Type: PlanTodo
project: Maliang 马良
status: 未开工
date: 2026-09-30
related: "TechPlan.md, Phase3Design.md"
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
| 3 | M2 | 「物」AI 生成管线:看图、精修、建模、绑骨动画、声音、失败判定(设计见 [Phase3Design.md](Phase3Design.md)) | 0(可与 1、2 并行) |
| 4 | M3 | 烧:燃烧演出(假进度) | 2 |
| 5 | M4 | 「物」全流程闭环 | 3、4 |
| 6 | M5 | 「境」全流程 | 1、5 |
| 7 | M6 | 兜底、快速模式、桌面模式、打磨 | 5、6 |
| 8 | M7 | 打包与发布 | 7 |

---

## 阶段 0:工程与依赖

- [x] 安装 XR 相关包:OpenXR 1.18.0、XR Interaction Toolkit 3.6.1(XR Hands 暂不装,需要手部追踪时再加)
- [x] 项目设置:OpenXR 已加入 Standalone 加载器,启用 Oculus Touch / Valve Index / HTC Vive 手柄配置;Windows 图形 API 改为 **Vulkan(首选)+ D3D12**;色彩空间已是 Linear;渲染模式 Single Pass Instanced(头显实测在阶段 1)
- [x] 安装 glTFast 6.20.0(运行时加载 GLB)
- [x] 引入 `wuyize25/gsplat-unity`(锁定 commit `a2bf458`),在 Unity 6.6 + URP 下编译通过;`GsplatURPFeature` 已加入 `PC_Renderer`(6.6 里 Render Graph 兼容模式设置已废弃,无需再关)
- [x] 建立目录 `Assets/Maliang/{Core,Drawing,Ritual,Api,Loading,Fallback,VR,Desktop,Art}` 与 `StreamingAssets/{Prompts,Fallback}`
- [x] 从 `ReferenceProject/Assets/Node_Brush` 拷贝资源到 `Assets/Maliang/Art/NodeBrush/`:笔模型 + 动画、笔触贴图、`penDraw.shader`、书桌 FBX 与贴图、印章插画、材质(**材质是内置管线的,进 URP 会显示粉色,阶段 2 搭场景时转换**)。脚本没有拷,阶段 2 重写
- [x] 补 `.gitattributes`:`.glb`、`.gltf`、`.spz` 加入 LFS 规则
- [x] 配置系统骨架:`MaliangConfig`(`Assets/Maliang/Core/MaliangConfig.cs`)读取工程根目录(打包后 exe 旁)的 `maliang.config.json`,缺失则离线模式;`Assets/StreamingAssets/maliang.config.example.json` 已提交
- [x] 日志封装:`MaliangLog`(分级、写 `persistentDataPath/maliang.log`、自动遮蔽已注册的 API 密钥)
- [x] 验证 Unity MCP 可用(已连通;本阶段的包安装、渲染器与 XR 设置、场景创建都是通过 MCP 完成的)
- [x] 导入 XRI 示例 Starter Assets 与 XR Interaction Simulator,建冒烟场景 `Assets/Maliang/Scenes/XRSmokeTest.unity`(XR Origin + 模拟器)
- [ ] **需要你在头显上做**:重启 Unity(让 Vulkan 生效)→ 连接头显 → 打开 `XRSmokeTest` 场景 → 播放

**验收:** 空场景能在头显里进入并看到手柄;工程无编译错误;Vulkan/D3D12 下运行正常。
(编译无报错已验证;头显那一项需要真机,见上面最后一条。)

---

## 阶段 1(M0):Splat Spike —— 最大风险,先做

**已就绪的工具:** 场景 `Assets/Maliang/Scenes/M0_SplatSpike.unity`(XR Origin + `SplatWorld`)。`SplatWorldLoader`(加载 SPZ / 碰撞体 GLB、应用尺度与地面偏移)和 `SplatSpikeController`(头显内 HUD + 手柄调参)在 `Assets/Maliang/Loading/`。
**手柄:** 右 A 切换坐标约定并重载 · 右 B 绕 Y 转 180° · 左 X/Y 放大/缩小 · 左右摇杆按下 降低/升高世界 · 双手握把重置。HUD 显示 fps、最差帧、点数、坐标约定、图形 API、头显名。每 5 秒写一条数据到 `maliang.log`(`%USERPROFILE%\AppData\LocalLow\DefaultCompany\My project\maliang.log`)。

- [x] 测试数据:`TestData/Splats/`(git 忽略)里有 `worldlabs_test.spz`(取自 VRSplatScene,World Labs API 输出,30MB)和 `spaceship_cabin.spz`;改 `SplatSpikeController.spzPath` 可切换
- [ ] 用 World Labs 手动生成/导出一个世界:`full_res` SPZ、`500k` SPZ、碰撞体 GLB、`semantics_metadata`(目前只有 `Downloads` 里旧的 `Futuristic Spaceship Cabin Interior.spz`,2M 点、SH 0 阶,没有碰撞体和元数据)
- [x] 在 Unity 中 `LoadFromSpz(path)` 加载到 `GsplatRenderer`:2,000,000 点解码约 0.4 秒,编辑器内无报错
- [x] **先重启 Unity**:改图形 API 后编辑器仍在跑 D3D11,此时 Gsplat 加载成功但什么都不画。重启后编辑器为 Vulkan 1.1(RTX 5070 Ti Laptop)
- [x] **关闭玩家重力**:Spike 场景没有地面,XRI 的 `GravityProvider` 会让 XR Origin 一直下落、离开 splat。已在 `M0_SplatSpike` 中把 `GravityProvider` 与各 Move Provider 的 `m_UseGravity` 关掉(接上碰撞体后可再打开)
- [x] 头显里能看到 splat(Single Pass Instanced,`worldlabs_test.spz`,2M 点)
- [ ] 头显实测:Single Pass Instanced(默认)是否黑屏/双眼错位;不行再试多通道(OpenXR 设置里改 Render Mode)
- [ ] 头显实测帧率:`full_res`(约 2M 点) vs `500k`,记录显卡型号
- [x] 坐标转换:暂定 **RDF**(`worldlabs_test.spz` 方向正确);手调参数暂定 `metricScaleFactor=2`、`groundPlaneOffset=-1`,待有 `semantics_metadata` 后替换
- [ ] 应用 `metric_scale_factor` 与 `ground_plane_offset`,使玩家站在地面上、尺度合理(需要 `semantics_metadata`;没有就先用手柄手调并记录数值)
- [ ] 碰撞体 GLB:`SplatWorldLoader.LoadColliderAsync` 已写好,填入 `colliderGlbPath` 后验证站立/传送落点(需要匹配这个世界的 GLB)
- [ ] 若 SPZ 解码异常,试 SPZ → PLY 走 `LoadFromPlyBytes` 备选(目前 SPZ 解码正常)
- [ ] 写 Spike 结论:可用的渲染模式、推荐点数档位、变换参数

**验收:** 头显里无黑屏、无双眼错位,帧率达标;站在地面上,碰撞体对齐。

**失败预案:** Gsplat 在头显不可用 → 试备选 GaussPlatUnity;仍不行 → 「境」降级为桌面模式渲染 / 500k 档 / 预生成展示,并回头修订 TechPlan §8。

---

## 阶段 2(M1):画 + 印 + 升空

**场景:** `Assets/Maliang/Scenes/M1_Desk.unity`,由 `Maliang > Build M1 Desk Scene`(`Assets/Maliang/Editor/DeskSceneBuilder.cs`)生成,改布局请改脚本后重新生成,不要手改场景。
**调试键:** `R` 或左手柄菜单键 = 重置卷轴;`E` = 导出墨迹到 `TestData/Exports/`。
**编辑器内测试(无需头显):** `CanvasTestPainter.PaintMountainScene` 走和 VR 毛笔相同的笔触路径;`SealStamp.TryStamp()` 可直接盖章。

### 2.1 场景与画布
- [x] 桌面场景:中式书桌(FBX 原本斜 45° → 旋转 -135°;椅子保留但不加碰撞体;隐藏会遮挡卷轴或看起来可抓的装饰:笔筒、散笔、笔搁、毡垫、镇纸)。10 色颜料(墨/朱砂/石青/石绿/藤黄/赭石/花青/曙红/朱磦/紫)两排放在右前方,颜料碟为深色石材(程序生成的石纹贴图 + 法线);两枚印章(左前)、毛笔(颜料后方)。可抓取摆件:两本书(封面+书页一起)、左侧石砚盘、古砚、红木砚托,松手回原位。桌面高 0.74 m,前沿在 z=0.38
- [~] 卷轴:平面画布 + 左右两根木轴(占位)。**展开动画未做**(开局即展开,`Rolled` 状态暂不使用)
- [x] 画布:**固定水平平面**,`InkCanvas.TryProject`(`InverseTransformPoint`)→ UV → RT 像素;尺寸 0.72×0.36 m

### 2.2 毛笔(移植 Node_Brush)
- [x] `InkCanvas` + `BrushStroke` 取代 `PaintingHandler`:去单例、画到卷轴材质、**无撤销**、只保留清空;GL 绘制前显式设 `GL.Viewport`(不设会画错位置/清不全)
- [x] Ink RT:不透明白底,深度 0,2048×1024(sRGB)
- [x] 保留贝塞尔插值、速度变粗细、随机毛边;常量按像素比例缩放;同一段笔触的印点合批成一次 GL 绘制
- [x] `BrushPen`:笔尖投影到画布,高度 0~2 cm 内作画,越低越粗(可穿透纸面 4 cm 仍按最粗);**笔画粗细系数默认 0.3**(参考项目是写字用的,原值太粗)
- [x] 笔头骨骼动画(`BrushRiggedAnimator`,X/Y 参数)、`InkPot` 蘸色(触发器)、笔触贴图(默认 `brushTexture`)
- [x] 笔毛单独上色:`BrushMeshSplitter` 把笔网格拆成 笔杆(槽 0)/笔毛(槽 1)两个子网格(`Assets/Maliang/Art/Brush/BrushSplit.asset`,笔尖起 2 cm;4 cm 会误切到装饰环),蘸色时只改笔毛材质;去掉了遮挡笔尖动画的 InkTip 小球
- [x] XR 抓取:`XRGrabInteractable`(即时移动、动态抓取点、不投掷),松手自动回到原位(`GrabbableTool`);蘸色/盖章有手柄震动
- [x] 卷轴显示 Shader `Maliang/ScrollDisplay`:`Paper × Ink` 再叠 Seal,双面
- [ ] **头显实测**:抓笔手感、笔尖高度阈值、粗细系数、蘸色触发

### 2.3 导出
- [x] `AsyncGPUReadback` 读 Ink RT
- [x] 墨迹包围盒 + 9% 边距 → 补白到卷轴比例(不拉伸)→ 长边 1536 → PNG(已验证输出正确,无印章无纸纹)
- [x] 墨迹面积:64×32 粗网格覆盖率(同步,盖章检查用),阈值 `ScrollRitual.minInkCoverage` = 1%

### 2.4 印章
- [x] 两枚印章(石料方块 + 钮 + 侧面标签),印面贴图 `Assets/Maliang/Art/Seals/seal_wu.png`、`seal_jing.png`(PIL + 楷体生成的占位图)
- [x] 印面 4 角投影到卷轴 → UV → `GL.QUADS` 画入 Seal 层;已验证旋转 15° 盖章时方向、大小与实体一致,不镜像
- [x] 触发:印面中心离纸 < 0.8 cm 且倾角 < 35°,抬高 3 cm 后才能再次触发
- [x] **盖章前检查墨迹面积**,不足则不落印、不升空,手柄轻震(已验证空卷轴盖章被拒)
- [x] `SealType`(物/境)作为数据记录在 `ScrollRitual.Seal`
- [x] 盖章生效:锁定画布输入 → 停 0.8 s → 先直升 15 cm 再滑到头前 0.6 m、眼下 0.18 m,朝向玩家并后仰 12°,上下轻微浮动(已验证悬停画面正立、不镜像)
- [x] 手柄震动反馈
- [ ] **头显实测**:盖章判定阈值、升空位置是否手可及(阶段 4 要用烛火去点)

**验收:** 导出图无印章、无纸纹、比例正确;盖章姿态与实体一致;空卷轴盖章不生效;盖章后卷轴稳定升空。
(编辑器内全部通过;头显手感待测。)

---

## 阶段 3(M2):「物」AI 生成管线(可与阶段 1、2 并行)

> 完整设计见 [Phase3Design.md](Phase3Design.md)(决定 D1~D21)。游戏内文字一律英文。

**先做的实测(Phase3Design 第 13 节):**
- [x] glTFast 运行时载入带骨骼动画的 Tripo GLB 并播放(马、道士通过)
- [x] P1-20260311 与 v3.1-20260211 的模型质量、面数对比:维持生物 P1、静态 v3.1 限面数(模型不需要水墨风格)
- [x] gpt-image-2.5-sunburst 精修 5 张测试画:主体与颜色特征是否保留、姿势要求是否遵守(精修图不需要水墨风格;共测 6 张:锦鲤、鹤、灯笼、道士颜色和特征都保留,道士去掉手杖、露腿、A 姿势,背景纯灰)
- [x] 识别尺度:20 张合成测试画,清楚和写意 15 张全部成功,乱涂 3/5 失败(另 2 张被宽松解读);2026-10-01 收紧(证据式识别 + `confidence` 门槛 0.6)后重测:15/15 成功,乱涂 5/5 失败;之后用 VR 真实画补测
- [ ] Windows 打包版运行时解码 ElevenLabs 的 MP3(编辑器里已通过)

**实现:**
- [x] 用我们的 OpenAI key 查 `/v1/models`,定下 GPT 文本模型型号(D10):`gpt-6.1-sol`
- [x] `Http`:超时、有限次指数退避重试、错误归类到 `FailReason`(unreachable / exhausted / forbidden / collapsed)
- [x] `VisionClient`:水墨 PNG + 印章类型 + 能力表 → 严格结构化输出 `VisionPlan`(`ok` / `fail` 二选一);提示词放 `StreamingAssets/Prompts/vision_object.txt`;按允许列表校验返回内容
- [x] `ImageRefineClient`:`/v1/images/edits` + `gpt-image-2.5-sunburst`,出错跳过
- [x] `TripoClient`:上传 → 建模 → 轮询 → 立即下载;rig-check(免费)→ rig(人形 v1.0、其他 v2.5)→ retarget(烘焙动画、原地播放)
- [x] 绑骨把关:GPT「该不该动」×预检「能不能动」;鸟类不绑骨;绑骨或套动作出错退回静态模型
- [x] `SoundClient`:ElevenLabs `eleven_text_to_sound_v2`,和建模并行,出错就无声(需要 ElevenLabs key)
- [x] `ObjectAgent` + `ObjectJob`:编排各步骤、并行声音、结果判定、失败归类;盖章时启动
- [x] `ObjectSpawner`:载入 GLB、统一尺寸、可抓取、播放动画或 `ProceduralMotion`(静态浮动 / 鸟类飞行)、声音触发(化形时 / 被抓时 / 循环)
- [x] `maliang.config.json` 新增 `sound`、`library` 配置段,`imageGen` 与 `tripo` 加模型字段
- [x] 编辑器测试入口:选一张 PNG,不进头显跑完整条管线,产物写到 `TestData/Agent/`(菜单 Maliang/Agent;老虎实测 2 分 41 秒跑通,运行模式生成后动画、声音、抓取正常)
- [x] 会话内生成次数限额

**验收:** 画清楚的人、马、鱼各自得到会动的物体;画鹤得到带程序飞行的静态模型;画灯笼得到静态物体;至少一个有合适的声音;乱涂的画返回 `fail`;各类失败都能正确归类。

**验收结果(2026-10-01,Unity 编辑器内 5 条并行):** 通过。锦鲤 137 秒(aquatic 绑骨,游动动画);道士 136 秒(人形绑骨,idle + bow 两段动画合并);鹤 143 秒(静态 v3.1 + 程序飞行,被抓时鸣叫 2.6 秒);灯笼 159 秒(静态,浮动);乱涂 5 秒判 `too_abstract`,显示英文提示语。马、老虎之前已通过。网络类失败归类由 `Http.Classify` 覆盖,未做断网实测。
- [ ] 化形朝向:生成物不一定面向玩家(道士侧身)。Tripo 模型的正面朝向不固定,需要按包围盒或 rig 的朝向校正,阶段 5 接入仪式时处理

---

## 阶段 4(M3):烧

- [x] Burn / Dissolve Shader:采样合成结果(纸 × 墨 + 印);**燃烧起点取火焰接触点的卷轴 UV**;最多 16 个燃烧点,各自按圆扩散加噪声边缘(发光边、焦黑带、焦黄圈);未点着的点先显示成变黄的焦斑
- [x] 多点燃烧:除烛火点燃点外,随燃烧推进随机出现新燃点(约 65% 是落在火线前方的火星,其余随机落在纸上),先焦黄约 2 秒再起火
- [x] 进度 = 已烧面积比例(64×32 网格估算),只增不减;驱动方给 `TargetProgress`,火的时钟跟随,目标停住火就停住阴燃(为 40% 等待点准备)
- [x] 卷轴两侧纸烧穿后木轴掉落(刚体),约 4 秒后消失;重置时复原
- [x] 火焰粒子、灰烬、火星、烟、火光点光源(沿火线发射;火被压住时变小变少)
- [x] 编辑器燃烧测试:菜单 Maliang/Debug/Burn Test (Play Mode),自动铺卷轴、画、盖印、烛火点燃,按进度截图到 `TestData/Burn/`
- [x] 可抓取烛台:莲花烛台 + 红烛 + 火焰粒子与闪烁光源,放下后回到桌边
- [x] 火焰触发体:`CandleFlame.All` 登记所有火焰,卷轴每帧检测火焰到纸面的距离
- [x] 点燃判定:卷轴处于 `Levitating` 且已浮空 + 火焰距纸面 2 cm 内持续 0.35 秒(短碰只留焦斑,离开后消退;接触时手柄轻震,点着时强震);点着后烛火不再与这张卷轴交互
- [ ] 卷轴状态机落地:`Rolled → Unrolled → Levitating → Burning → Materializing → Done/Failed`(已接入到 `Burning → Done`;`Materializing`、`Failed` 待做)
- [ ] 虚拟进度器:单调不减;按 TechPlan §7.2 分阶段预算;主生成阶段渐近曲线;真实完成后快进
- [ ] 「境」用里程碑事件(火势分批加大、卷轴分批脱落、环境音变化)
- [x] 先用**假计时器**驱动完整燃烧演出(`selfTimed`,默认 14 秒;桌面调试键 B 直接点燃浮空卷轴)
- [ ] 燃烧等待点:烧到 40% 时 GPT 结果未到就放慢火势、停住等待;成功继续,失败转失败演出(Phase3Design D12)
- [ ] 回放节奏:固定 10 秒燃烧,末尾余烬状态等待本地载入完成(Phase3Design 8.5)
- [ ] 失败演出(Phase3Design 第 7 节):火势停滞冷却 → 魔力消失 → 落地 → 英文提示语;`Burning → Failed`;释放悬停区
- [ ] 残卷:物理刚体、可捡可扔、约 12 秒后化灰销毁
- [ ] 失败演出用的预制音效:火苗熄灭、泄气、落地闷响
- [ ] TextMeshPro 拉丁衬线字体的世界空间提示文字(不引入中文字体)

**验收:** 视觉上进度与阶段吻合;无论假进度提前或延迟完成,演出都不突兀;失败演出完整,残卷能扔、会化灰。

---

## 阶段 5(M4):「物」全流程闭环

- [ ] 盖章 → `ObjectJob` 启动 → GPT 看图;点燃后按结果走成功或失败;成功则精修 → Tripo → 绑骨动画,声音并行
- [ ] `ObjectJob` 进度驱动 `_BurnProgress`(过了 40% 等待点之后)
- [ ] 燃尽处化形:GLB 出现在卷轴悬浮位置,可伸手抓取,动画和声音正常
- [ ] 作品库写入(Phase3Design 第 8 节):盖章时存墨迹层和印章层到临时目录,成功后整体改名为正式目录并更新索引;失败删除临时目录;声音晚到时补写
- [ ] `LibraryDrawer`:按索引在左抽屉(物)/右抽屉(境)生成回放卷轴,各显示最新 6 卷;红 / 青丝带与印章纸签区分;新作品到来时补一卷
- [ ] 回放模式:放到画画位 → 展开显示原画和原印章(`InkCanvas.LoadLayers`)→ 锁定作画与盖印 → 自动浮空 → 点燃烧 10 秒 → 载入本地 GLB、动画、声音并化形 → 卷轴回到抽屉原位
- [ ] 作品文件损坏或丢失时走失败演出,提示语 `faded`
- [ ] 失败路径:重试 → 仍失败则进入兜底(阶段 7 前先用一个内置模型顶替)
- [ ] 全流程手测 10 次,记录耗时与失败率
- [ ] (加分项)Tripo 多视角参考图,不阻塞首版

**验收:** 画 → 盖「物」印 → 点燃 → 等待 → 拿到自己画的东西,全流程打通;重启游戏后作品仍在左抽屉里,回放结果和当初一致。

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
- [ ] 「境」作品写入作品库(splat、碰撞体、对齐参数、语义信息),右抽屉回放后进入同一个世界;实测 splat 体积并定 `library.maxDiskMB` 默认值

**验收:** 玩家可站在生成的世界里移动,并能回到桌面。

---

## 阶段 7(M6):兜底、快速模式、桌面模式、打磨

- [ ] 预生成 3~6 组资源(剑、龙、山、城堡等),直接用作品库格式放入 `StreamingAssets/Library/`(Phase3Design 8.8),首次启动抽屉里就有卷轴
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
- [ ] 待确认项(TechPlan §14):兜底内容清单、评委是否有头显、发布渠道、是否首版做 Tripo 多视角(VLM 已定为 OpenAI,见 Phase3Design)
- [ ] 作品的删除方式(Phase3Design 第 15 节,尚无设计)
