# PCL for CAUMC

CAUMC 社区维护的 PCL 第三方修改版，基于龙腾猫跃的 [Plain Craft Launcher](https://github.com/Meloong-Git/PCL)。

[下载最新版](https://github.com/apshiap05/community-PCL-for-CAUMC/releases/latest) · [服务器指南](https://doc.caumc.cn/?post=2) · [皮肤站](https://skin.caumc.cn/)

- 内置 CAU 皮肤站登录，打开即可用。
- 主服、模组服、皮肤站指南随网站更新，互链直接切换。
- 模组服指南中的 ZIP 整合包可直接下载并导入。
- 统一玻璃界面，22 张社区截图每 5 秒轮播。
- 保留游戏下载、整合包导入、Mod、版本管理、设置和诊断。

## 使用

Windows 10 / 11，需 .NET Framework 4.8。下载 EXE，放进一个文件夹运行。整合包可以直接拖入窗口。

游戏目录和普通配置兼容原版 PCL。首次打开可导入旧游戏，之后也可从「版本选择」左侧底部进入。导入会复制文件并保留原游戏；账号缓存分别保存。

微软登录等待 Minecraft AppID 审核；CurseForge 官方源待接入。

## 构建

安装 .NET 10 SDK、Visual Studio 的 .NET 桌面构建工具和 .NET Framework 4.8 目标包。

```powershell
git clone --recurse-submodules https://github.com/apshiap05/community-PCL-for-CAUMC.git
cd community-PCL-for-CAUMC
./build.ps1 -RunTests
```

产物：`dist/PCL for CAUMC.exe`。

基于 PCL `6b9a50d`（2.99.0.0）。许可见 [LICENCE](LICENCE)，组件署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。[支持原作者龙腾猫跃](https://meloong.com/afd/a/LTCat)。
