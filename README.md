# 黄昏琴村 Twilight Keys

PICO 4 Ultra 上的混合现实（MR）节奏钢琴游戏。不用手柄，只用手和眼睛：

- 琴摆在你面前的真实桌面上，手指敲桌面就是弹琴；
- 墙上开出一扇“窗中村”，随着你弹奏，黄昏的村庄从窗口一点点铺满整个房间，弹完时你坐在湖边草地上；
- 一只八音盒负责选歌和设置，看着窗框上的歌名捏一下，就能把歌单拿出来。
- 琴两边各有一颗圆钮：双手各捏一颗，就能把琴拖到你自己的桌子上摆正（也可以在设置里用两根手指按住桌边一键对齐）；挪琴时窗外的世界不会跟着晃，转得太偏时场景会“眨一下眼”移到琴的正前方。

曲库 37 首，分古典、浪漫、民歌、童谣四类，每首都有“大师演奏”可以先听一遍。界面中英双语。第一次玩有新手教学（练习曲《斯卡布罗集市》），之后可在设置里重新打开。

## 安装

到本仓库的 [Releases](../../releases) 下载最新的 `TwilightKeys_*.apk`，用数据线连上头显：

```bash
adb install -r -g TwilightKeys_xxxx.apk
```

首次进入需要允许手部追踪、眼动追踪和空间数据权限（`-g` 会自动授予）。

## 从源码构建

- Unity **6000.5.5f1**，URP
- PICO Unity Integration SDK **6.0.0**：`Packages/manifest.json` 里 `com.bytedance.pico.xr` 指向本机路径，请改成你自己的 SDK 位置
- 主场景：`Assets/_Twilight/Scenes/Main.unity`
- 菜单 `Twilight/1 搭建主场景` 会按脚本重建整个场景；打包入口是 `Twilight.EditorTools.TKBuild.BuildApk()`

目录：

| 路径 | 内容 |
|---|---|
| `Assets/_Twilight/Scripts/Logic` | 纯 C# 玩法核心：谱面解析、判定、敲击检测（不依赖 Unity 场景，可单测） |
| `Assets/_Twilight/Scripts/Runtime` | 表现层：光河、音块、八音盒、歌单、窗中村铺开、手部遮挡等 |
| `Assets/_Twilight/Scripts/Editor` | 场景搭建、打包、回归测试 |
| `Assets/_Twilight/Resources/songs.json` | 曲库（谱面 + 大师演奏编曲） |
| `Tools/` | Blender 建模脚本、字体子集化、钢琴采样处理等离线工具 |

## 致谢与授权

- **钢琴音色**：[Salamander Grand Piano V3](https://github.com/sfzinstruments/SalamanderGrandPiano)，作者 Alexander Holm，[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)。取 v10 力度层，经剪切起音前静音、相邻键移调 ±1 半音、音量对齐、3.6 秒淡出后转为 mp3（处理脚本：`Tools/make_piano_samples.py`）。
- **曲目**：全部为公有领域作品（作曲家均已去世 70 年以上，或为传统民歌、童谣），谱面为本项目自行编配。
- **字体**：霞鹜文楷、马善政、Cormorant Garamond、Nunito，均为 SIL Open Font License 1.1，游戏内使用子集化后的版本，授权文本见 `Assets/_Twilight/Resources/Fonts/`。
- **八音盒音色**：程序实时合成，不使用采样。
- **玩法原型**：来自团队的网页 demo《Twilight Keys》（VRGame04），本工程是它的 PICO MR 手眼追踪版。
