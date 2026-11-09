# 黄昏琴村 MR · Unity 正式工程方案（v1，2026-10-30）

工程：`/Users/ld/Documents/ChatGPT/TwilightKeys`（Unity Hub 名 TwilightKeys；英文路径，安卓打包不能有中文路径）。
Unity 6000.5.5f1 + URP 17.5 + PICO SDK 6.0.0（手部追踪、眼动、视频透视已开），配置复制自 WarTable（再往上是 BANG）。
包名 `com.toybox.twilightkeys`，产品名“黄昏琴村”。

玩法原型（**只作规则参考，画面和 UI 不照搬**）：`~/twilight-keys-xr`，规则见其 `MR设计.md`（第十节起是实现记录）。
命令行驱动 Unity：`~/.unity/twilight-tools/`（`cs.py` 执行 C#、`refresh_wait.sh`、`compile_check.sh`、`unity_call.py`）。编辑器日志 `Logs/editor.log`。

## 顺序（用户定）
1. **场景**：桌上的琴、光河与音块、墙上的窗与窗中村、天空、天灯、白手套、特效，全部做到好看为止。
2. **玩法**：照原型（4 轨敲击判定、延迟校准、宽容判定、暂停回拨、旋律按键、自由弹奏、注视天灯、窗户贴墙、天空随进度铺开），纯逻辑移植成 C# 并带单测。
3. **UI**：最后做；按头显重新设计，**去掉大段文字**，能用图标和动效表达的不写字。

## 用户硬要求
- 场景要好看、UI 要精致，**不照搬网页 demo 的画面和 UI**。
- 美术图一律 **Tripo nano banana**（banana_pro / banana2）；模型用 **Tripo P2**：
  参考图 → 四视图 → **白模**（texture=false）→ **查漏面 / 破面**（`Tools/mesh_audit_p2.py` + 六视图人工看）→ 合格才按参考图**贴图** → 清理 / 补洞 / LOD。
  贴图 **≤ 2048**，也**不能糊**（近景渲染核对）。复杂结构拆部件分别生成。
- 精确几何（琴键、琴身、窗框边线等）用 Blender 精细建模（倒角、细节层次、像样的材质），不交给 P2。
- **手指敲桌面时，头显的摄像头要看得到手**：琴摆在自然低头就能看到的位置；手部追踪丢失时要有提示（“看着琴键”），判定对追踪抖动要宽容。
- 一切以玩家舒适为先：不逼玩家举手、失败不自动重来、手势判定宽容、提示图标要画对。
- 真机不开后处理（MR 透视要 alpha + 性能），画面靠材质和打光。

## 场景构成与尺寸（MR，单位米）
| 部分 | 尺寸 / 位置 | 做法 |
|---|---|---|
| 桌上的琴 | 两八度 C4~B5，白键 23.5 mm × 14 ≈ 33 cm；含琴颊约 39 cm 宽、22 cm 深 | Blender 精细建模：胡桃木琴身 + 黄铜细线 + 红毡条 + 象牙白键 / 乌木黑键（带倒角）；琴键可单独下沉；4 条轨道 = 键盘宽度四等分 |
| 光河 | 从窗台流到琴键后沿，长 1.2~3 m，略向下倾 | 半透明“液态暮光”缎带着色器（流动焦散、细闪）+ 黄铜细线分道 |
| 音块 | 每拍长度按原型 18 cm 等比，宽约一道 | 磨砂玻璃块 + 内光，按轨道色（琥珀 / 珊瑚 / 蔷薇 / 暮蓝） |
| 墙上的窗 | 约 1.2 × 0.9 m，贴真实墙（平面检测）或悬在 2 m 处 | P2 木窗框（推开的彩绘百叶 + 花箱）；窗洞用模板做传送门 |
| 窗中村 | 真实尺度，在窗后 30 m~3 km | P2 小屋 / 风车 / 钟楼 / 码头 / 小船 / 树 / 灯柱 / 芦苇 + 程序化山丘与湖面；屋窗随入夜亮灯 |
| 天空 | 黄昏 / 晚霞 / 入夜三张全景 | nano banana 画全景天空，天空球着色器按进度从窗口铺开 |
| 天灯 | 约 12 cm | P2 |
| 白手套 | 跟手 | PICO 手部模型 + 棉布手套材质（nano banana 微距棉布 → 无缝贴图） |
| 特效 | — | 萤火、命中火花、桌面涟漪、烟花 |

## 美术流程与目录
- 概念图 `ArtSource/Concept/`（`queue.json` 串行生成：`python3 -I Tools/gen_images.py ArtSource/Concept/queue.json`）。
- 资产表 `ArtSource/specs.json`（`{"名称": {"faces": 面数上限}}`），参考图 `ArtSource/Refs/<名称>/image.png`，进度 `ArtSource/ModelsP2/<名称>/state.json`。
- 跑：`Blender -b -P Tools/p2_pipeline.py -- run <名…>`；审批 `-- approve / reject`；收尾 `python3 -I Tools/p2_finalize.py <名…>`。
- Unity 侧代码与资源都在 `Assets/_Twilight/`。
