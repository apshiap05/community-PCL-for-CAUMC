# PCL for CAUMC

CAUMC 社区维护的启动器，基于龙腾猫跃的 [PCL](https://github.com/Meloong-Git/PCL)。

[启动器下载](https://doc.caumc.cn/?post=7) · [服务器指南](https://doc.caumc.cn/?post=2) · [皮肤站](https://skin.caumc.cn/)

- 内置 CAU 登录，服务器指南随网站更新。
- 模组服整合包一键下载、导入与更新提醒，新包独立安装并保留旧版。
- 任务中心支持进度恢复、失败重试和后台下载。
- 启动器确认后联网更新，安装失败自动回退。
- 玻璃界面，22 张社区截图每 5 秒轮播。
- 支持 CurseForge 官方源、镜像和 Modrinth，保留 PCL 的下载、版本管理与诊断功能。

## 使用

Windows 10 / 11，需 .NET Framework 4.8。下载 EXE，放进文件夹运行；整合包可直接拖入窗口。默认游戏内存 6 GB，已有配置继续生效。

兼容原版 PCL 的游戏目录和普通配置。首次打开可导入旧游戏，之后入口位于「版本选择」左侧底部；原游戏保留，账号缓存分别保存。

## 构建

需要 .NET 10 SDK、Visual Studio .NET 桌面构建工具和 .NET Framework 4.8 目标包。

```powershell
git clone --recurse-submodules https://github.com/apshiap05/community-PCL-for-CAUMC.git
cd community-PCL-for-CAUMC
./build.ps1 -RunTests
```

产物：`dist/PCL for CAUMC.exe`。CurseForge 凭据从 `PCL_BUILD_CF_API_KEY` 或本机 `.build/curseforge-api-key.txt` 注入。

发布更新：在[启动器页面](https://doc.caumc.cn/?post=7)上传 EXE，链接文字写为 `PCL-for-CAUMC-版本号.exe`，与程序版本一致；启动器选择最高正式版。

基于 PCL `6b9a50d`（2.99.0.0）。许可见 [LICENCE](LICENCE)，组件署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。[支持原作者龙腾猫跃](https://meloong.com/afd/a/LTCat)。
