# Number Cover 外部配置控制器兼容性设计

日期：2026-07-15

## 背景与根因

Number Cover 已能被 Jellyfin 10.11.11 加载，配置页也能显示表单控件，但保存的配置值没有填入页面。现有配置页依赖内联 `<script>` 注册 `pageshow` 事件并调用 Jellyfin API。

Jellyfin Web 10.11.11 的页面装载逻辑只会在存在全局 jQuery (`window.$`) 时通过兼容分支执行插件页中的内联脚本；当前运行环境没有该全局对象，因此 HTML 被插入页面后，内联脚本不会执行。结果是配置读取、表单填充和保存处理均未注册。

## 方案比较

### 方案 A：Jellyfin 外部页面控制器（采用）

使用 Jellyfin Web 已支持的 `data-controller="__plugin/..."` 协议，将配置逻辑放入插件内嵌的 JavaScript ES 模块。Jellyfin 会解析插件资源 URL、动态导入模块，并在页面初始化阶段实例化控制器。

优点：遵循 Jellyfin 10.11 的原生插件页面机制；不依赖全局 jQuery；脚本与页面实例明确绑定；插件可离线部署。

### 方案 B：在插件页恢复或打包 jQuery（不采用）

这会引入额外体积、旧式全局依赖和潜在版本冲突，也不能保证 Jellyfin 页面装载器会走预期兼容分支。它没有解决插件应使用正式控制器接口这一根本问题。

### 方案 C：绕过页面，直接编辑 XML 配置（不采用）

这只能临时规避 UI 问题，用户体验差，并可能绕过 Jellyfin 的配置校验、缓存刷新和生命周期处理。

## 架构设计

1. 配置页根元素增加 `data-controller="__plugin/NumberCoverConfigJs"`。
2. 从 `configPage.html` 删除全部内联脚本，使 HTML 只负责结构和控件声明。
3. 新增嵌入资源 `Configuration/configPage.js`，默认导出一个可由 Jellyfin 使用 `new` 实例化的控制器函数。
4. `Plugin.GetPages()` 除现有 HTML 页面外，再注册一个名称严格为 `NumberCoverConfigJs` 的 `PluginPageInfo`，其资源路径指向 `Configuration.configPage.js`。
5. 项目文件将 `configPage.js` 声明为 `EmbeddedResource`，确保脚本随 DLL 一起部署。
6. 控制器只在 Jellyfin 传入的当前 `page` 元素内查询控件，避免 Jellyfin 缓存多个配置页实例时读写到旧页面。
7. 控制器构造时注册表单提交处理，并在当前页面的 `viewshow` 事件中读取配置。Jellyfin 的页面管理器会在控制器实例化后发送该事件。
8. 控制器通过 Jellyfin 提供的全局兼容接口 `window.ApiClient` 和 `window.Dashboard` 调用配置 API、显示加载状态和处理保存结果。

## 数据流

### 读取

1. 当前配置页触发 `viewshow`。
2. 显示 Jellyfin 加载指示器。
3. 调用 `ApiClient.getPluginConfiguration(pluginId)`。
4. 将 `EnableJableSource`、`OnlyFillMissing`、`MaxRequestsPerMinute` 和 `TimeoutSeconds` 写入当前页面对应控件。
5. 无论成功或失败都关闭加载指示器；失败时在页面内显示简短错误信息，并记录控制台错误供诊断。

### 保存

1. 用户提交当前页面的表单。
2. 先读取服务器上的现有配置，避免覆盖本页未展示的字段。
3. 将当前控件值合并到配置对象。
4. 调用 `ApiClient.updatePluginConfiguration(pluginId, configuration)`。
5. 使用 `Dashboard.processPluginConfigurationUpdateResult` 处理 Jellyfin 标准结果。

数字字段转换为整数；输入为空或无效时使用插件当前默认值（每分钟 12 次请求、超时 10 秒），防止发送 `NaN`。

## 错误处理

- 配置读取失败时不伪造“已加载”状态，页面展示错误提示。
- 保存失败时关闭加载指示器，并通过 Jellyfin 的标准错误处理或页面错误信息反馈。
- 所有 DOM 查询都限制在当前页面实例内。
- 不在配置页中直接访问、下载或解析 Jable 内容。

## 测试与验收

自动化测试覆盖：

1. HTML 包含正确的 `data-controller`，且不再包含内联 `<script>`。
2. `Plugin.GetPages()` 同时注册 HTML 页面和 `NumberCoverConfigJs` 脚本资源。
3. 项目文件嵌入 `configPage.js`。
4. JavaScript 默认导出控制器，使用页面范围查询，并包含配置读取与保存 API 调用。
5. 全部单元测试通过，Release 构建无错误。

实际 Jellyfin 10.11.11 验收：

1. 安装新 DLL 并重启 Jellyfin。
2. 打开 Number Cover 配置页，不出现资源加载错误。
3. 页面显示当前保存值：Jable 来源关闭、仅填充缺失封面开启、每分钟请求数 12、超时 10 秒。
4. 如需验证保存，仅保存原值或由用户明确指定的新值；刷新页面后值保持一致。

## 范围边界

本次修改只修复配置页脚本加载、读取和保存。它不改变番号识别、Jable 抓取、封面选择逻辑，也不会自动启用 Jable 来源或修改用户现有配置。
