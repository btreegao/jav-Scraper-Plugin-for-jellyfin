# Number Cover 0.2.2

Number Cover is a Jellyfin 10.11.11 plugin that recognizes identifiers such as `IPZZ-034` in a movie name or path and finds a primary cover. Sources are tried in order: R18.dev, JavDB.com, then deterministic DMM/FANZA. Jable has been removed.

Online sources and replacement of existing covers are enabled by default. The plugin does not download videos, require login, use browser cookies, or bypass access controls. Use it only where you have authorization and follow the applicable source-site terms.

## How it works

- `IPZZ-034` maps to `ipzz00034` and the exact DMM image URL `https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg`.
- Copy suffixes after an underscore are ignored only when the suffix begins with an ASCII letter (`[A-Za-z]`), so `MKMP-418_CH_SD` is normalized to `MKMP-418`; numeric continuations such as `ABC-123_456` remain rejected.
- DMM results are accepted only from the exact HTTPS host and canonical path, and only when the response is HTTP 200 with an image content type. Redirects and HTML are rejected.
- R18.dev is queried by identifier first. If no valid cover is found, JavDB.com is searched for an exact identifier match. The deterministic DMM/FANZA URL is the final fallback.
- JavDB images are restricted to HTTPS `jdbstatic.com` cover URLs. R18.dev images are restricted to HTTPS `pics.dmm.co.jp` digital or physical-release jacket paths. Both catalog sources verify HTTP 200 and an image content type before returning a result; redirects are not followed.
- Not every identifier is guaranteed to have a matching cover.
- **Only fill missing** preserves an existing primary cover.

Default settings are: online sources on, replace existing covers, combined request limit 12 per minute, and a 10-second timeout per request. All three sources share the limit; catalog page/API requests and image checks each count toward it. The existing online-source switch enables the full fallback chain. Saved settings from older installations are preserved; check them after upgrading.

## Install and use

1. Extract `NumberCover-v0.2.2.zip` into one dedicated Number Cover folder under Jellyfin's `plugins` directory, then restart Jellyfin. When upgrading, replace the old DLL in its existing folder. Do not keep duplicate Number Cover DLLs in other plugin folders.
2. Open **Dashboard > Plugins > Number Cover** and enable online cover sources.
3. Open **Dashboard > Libraries > Manage library** for each movie library. Under movie image downloaders, enable **Number Cover (Jable)** and move it to the top. This historical name identifies the current R18.dev/JavDB/DMM provider; Jable itself is not used.
4. For one item, use **Edit images > Search**. For a batch, refresh images and select **Replace existing images**. A plain library scan may preserve existing artwork.

Choose settings and sources only for content you are authorized to access, and comply with the source sites' terms.

## 一键更新所有封面

当前插件没有单独的“更新全部”按钮，但可以使用 Jellyfin 的媒体库批量刷新功能：

1. 打开 **控制台 > 插件 > Number Cover > 设置**，启用在线封面来源。
2. 如果需要覆盖已有封面，请关闭 **仅填充缺失封面（Only fill missing）**。
3. 打开 **控制台 > 媒体库 > 管理媒体库**，在电影的“图片下载器”中勾选 **Number Cover (Jable)** 并移到首位，然后保存。该历史名称仅用于兼容，实际来源为 R18.dev、JavDB、DMM/FANZA。
4. 选择 **刷新元数据**，使用“搜索缺失的元数据”模式，并勾选 **替换现有图片**。
5. 确认后等待任务完成。插件会逐部识别文件名中的番号，并按 R18.dev → JavDB.com → DMM/FANZA 顺序查找封面，找到有效图片后停止。

默认请求上限为每分钟 12 次。大型媒体库需要较长时间，建议先在小型测试目录中确认效果。只有能够识别番号且在线来源返回有效图片的项目会更新。

## 0.2.0 新增来源

- **JavDB.com**：从公开搜索页核对完整番号后读取封面，避免把相似番号的图片用于当前电影。
- **R18.dev**：按番号查询 JSON，依次检查 `large`、`large2` 封面地址，可补充部分实体版封面和带厂商前缀的 DMM 地址。
- 保留已有设置和 Jellyfin 图片提供器名称，无需重新选择提供器；启用在线封面来源后自动使用新增来源。
- 站点暂时不可用、限流或要求登录/验证时会继续回退；不会使用登录凭据或浏览器 Cookie。新增来源仍不保证每个番号都有封面。

## 0.2.1 来源顺序调整

优先查询 **R18.dev**，其次 **JavDB.com**，最后 **DMM/FANZA**。Jable 抓取实现和图片域名许可已移除，插件不会再访问该站点。

为兼容已有 Jellyfin 媒体库设置，历史提供器名称 `Number Cover (Jable)` 和配置字段 `EnableJableSource` 仍保留作为内部标识；它们控制的是当前三个来源，不代表仍启用 Jable。

## 0.2.2 默认设置与使用修正

- 新安装默认开启在线封面来源，并关闭“仅补缺失封面”，允许替换已有封面。
- 保留每分钟 12 次请求和每次请求 10 秒超时；全库处理可能需要较长时间。
- 设置页在配置加载成功前禁用保存，避免读取失败或提前保存时覆盖现有设置。
- 旧安装会保留已保存值；若无法更新封面，请确认插件为 Active、在线来源已开启、“仅补缺失封面”已关闭，且媒体库图片下载器中已勾选并优先排列 Number Cover。

## 项目说明与后续计划

这是我的第一个 AI 项目，已经由我亲自测试，当前版本可以正常运行。由于本阶段可用的 AI token 已经不足，下面这些功能需要等我有时间后继续开发：

1. 自动更新主演/演员姓名及相关信息。
2. 继续扩展番号覆盖和封面来源；0.2.0 已加入 JavDB.com、R18.dev，部分 `ABP`、`EBOD` 等番号的结果仍取决于来源收录及网络可用性。
3. 完善文件名识别规则；目前某些连续书写、没有 `-` 分隔符的文件名无法自动搜索。
