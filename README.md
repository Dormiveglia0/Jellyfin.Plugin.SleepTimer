# Jellyfin Sleep Timer

在 Jellyfin Web 播放器的齿轮设置菜单中增加“定时关闭”。用户可以选择快捷时间或输入预计播放分钟数，倒计时结束后自动暂停播放或退出视频。

## 功能

- 在视频播放设置菜单中增加原生单行“定时关闭”，倒计时仅显示在右侧状态位
- 内置 15、30、45、60、90、120 分钟快捷项，管理员可修改
- 支持 1–1440 分钟自定义时长
- 到时操作可选“暂停播放”或“退出视频”
- 按用户和设备隔离；同一用户在不同设备上的计时器互不影响
- 计时器由 Jellyfin 服务端执行，不依赖浏览器标签页的 `setTimeout`
- Web 端保留幂等的本地到时保护，服务器命令延迟时仍会执行操作
- 播放弹窗复用 Jellyfin 原生主题类，可跟随深色、浅色和自定义 CSS 外观
- 支持 Jellyfin 插件库安装和后续版本升级
- 简体中文和英文界面

## 兼容性

- Jellyfin Server：`10.11.x`
- 目标 ABI：`10.11.0.0`
- 运行框架：`.NET 9`
- 已按 Jellyfin Web `v10.11.11` 的播放控件结构适配

可用客户端：

- Jellyfin Web 浏览器端
- 使用 Jellyfin Web 的 Android / iOS 外壳客户端
- Jellyfin Media Player 等嵌入 Web UI 的桌面客户端

不支持原生播放界面，例如 Android TV、Roku、Swiftfin 等；这些客户端不会加载服务器注入的 Web 脚本。

## 通过插件库安装（推荐）

进入“控制台 → 插件 → 插件库”，新增仓库：

```text
名称：Sleep Timer
URL：https://raw.githubusercontent.com/Dormiveglia0/Jellyfin.Plugin.SleepTimer/main/manifest.json
```

保存后进入插件目录，找到 **Sleep Timer** 并安装。安装或升级后重启 Jellyfin，再对 Web 页面执行一次强制刷新。

## Docker 注意事项

确保 Jellyfin 的 `/config` 持久化，例如：

```yaml
volumes:
  - ./jellyfin-config:/config
```

插件由目录安装时会写入 `/config/plugins`，因此容器更新不会删除插件。Web 控件按以下顺序启用：

1. 如果已安装 [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)，使用运行时文件转换。
2. 否则，如果已安装 [JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector)，向其注册客户端脚本。
3. 两者均未安装时，只在 Jellyfin Web 的 `index.html` 中维护一段带 `BEGIN/END Sleep Timer Plugin` 标记的脚本标签；卸载时会移除该标记块。

推荐在 Docker 中安装 File Transformation，但它不是硬性依赖。启用了 `read_only: true` 的容器无法使用第 3 种回退方式；容器镜像升级后，回退注入会在插件下次启动时重新应用。

## 手动安装

1. 从 GitHub Releases 下载 `Jellyfin.Plugin.SleepTimer_1.3.1.0.zip`。
2. 停止 Jellyfin。
3. 在容器的 `/config/plugins` 下新建 `Sleep Timer_1.3.1.0` 文件夹。
4. 将 ZIP 中的 `Jellyfin.Plugin.SleepTimer.dll` 解压到该文件夹。
5. 启动 Jellyfin 并强制刷新 Web 页面。

## 使用

1. 开始播放视频并唤出播放控制栏。
2. 点击齿轮“设置”，在“清晰度”等选项旁点击“定时关闭”。
3. 选择“暂停播放”或“退出视频”。
4. 点击快捷时间，或输入分钟数后点击“开始计时”。
5. 激活后，设置菜单中的“定时关闭”会显示精确倒计时；再次点击可查看或取消。

“退出视频”会向当前 Jellyfin 会话发送原生 `Stop` 指令；Web 客户端会结束播放并离开播放器。

## 外观兼容

播放器弹窗不会绑定固定背景色或固定主题色，而是复用 Jellyfin 的原生样式入口：

- `.dialog`：弹窗背景和正文颜色
- `.listItem` / `.actionSheetItemText`：播放器设置菜单入口
- `.raised`：普通按钮
- `.button-submit`：选中状态和主操作
- `.emby-input`：自定义时长输入框
- `.buttonActive`：倒计时强调色
- `.secondaryText` / `.toast`：辅助文字和通知

因此 Jellyfin 内置深色、浅色主题以及覆盖这些原生类的自定义 CSS 会同步作用于插件。插件 CSS 只负责布局、间距、响应式和可访问性。

## 前端排查

安装或升级后必须重启 Jellyfin，并对 Web 页面执行一次强制刷新。开发者工具控制台应出现：

```text
[Sleep Timer] Client initialized. Open the player settings menu to use it.
```

也可以在控制台运行：

```javascript
window.JellyfinSleepTimer?.diagnostics()
```

结果会显示客户端版本、API 状态、已插入的菜单项数量和当前计时器状态。插件自身日志统一以 `[Sleep Timer]` 开头；来自浏览器扩展的 `content.js` 报错通常与插件无关，可以使用无扩展的隐私窗口交叉验证。

## 管理员配置

进入“控制台 → 插件 → Sleep Timer”：

- 快捷时间：逗号分隔的分钟数，最多 12 项
- 默认到时操作：暂停或退出
- 最长自定义时间：1–1440 分钟
- 是否允许用户输入自定义时间

配置只影响之后创建的计时器。插件升级、安装或卸载后需要重启 Jellyfin。

## 构建

需要 .NET 9 SDK：

```powershell
dotnet restore .\Jellyfin.Plugin.SleepTimer.sln
dotnet build .\Jellyfin.Plugin.SleepTimer.sln --configuration Release
```

编译结果位于：

```text
Jellyfin.Plugin.SleepTimer/bin/Release/net9.0/Jellyfin.Plugin.SleepTimer.dll
```

推送四段式版本标签（例如 `v1.3.1.0`）会触发 GitHub Actions：

1. 构建并打包插件 DLL。
2. 创建 GitHub Release 并上传 ZIP。
3. 计算 Jellyfin 要求的 MD5 校验值。
4. 自动更新 `main` 分支上的 `manifest.json`。

## 实现说明

- 受保护的 REST API 创建、查询和取消当前用户/设备的计时器。
- 后台服务每秒检查到期项，并通过 Jellyfin `ISessionManager.SendPlaystateCommand` 发送 `Pause` 或 `Stop`。
- 客户端 CSS 作为程序集嵌入资源提供，并通过 Jellyfin 的原生主题类适配自定义外观。
- 活动计时器保存在内存中；重启 Jellyfin 会取消尚未到期的计时器。
- 客户端脚本本身可匿名下载，但所有计时器 API 均要求 Jellyfin 身份认证。

## 致谢与许可

项目以 Jellyfin 官方 [jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template) 为基础，并参考了 File Transformation、JavaScript Injector 以及社区 [Jellysleep](https://github.com/jon4hz/jellyfin-plugin-jellysleep) 的公开集成方式。

依照模板和 Jellyfin 插件链接要求，本项目使用 GPL-3.0 许可证，详见 [LICENSE](LICENSE)。
