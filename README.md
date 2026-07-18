# Number Cover 0.1.2

Number Cover is a Jellyfin 10.11.11 plugin that recognizes identifiers such as `IPZZ-034` in a movie name or path and finds a primary cover. Jable is tried first; the fallback is deterministic DMM/FANZA cover lookup.

Online sources are disabled by default. The plugin does not download videos, require login, use browser cookies, or bypass access controls. Use it only where you have authorization and follow the applicable source-site terms.

## How it works

- `IPZZ-034` maps to `ipzz00034` and the exact DMM image URL `https://pics.dmm.co.jp/digital/video/ipzz00034/ipzz00034pl.jpg`.
- Copy suffixes after an underscore are ignored only when the suffix begins with an ASCII letter (`[A-Za-z]`), so `MKMP-418_CH_SD` is normalized to `MKMP-418`; numeric continuations such as `ABC-123_456` remain rejected.
- DMM results are accepted only from the exact HTTPS host and canonical path, and only when the response is HTTP 200 with an image content type. Redirects and HTML are rejected.
- Jable is tried first, followed by the deterministic DMM/FANZA fallback when Jable does not return a cover.
- Not every identifier is guaranteed to have a matching cover.
- **Only fill missing** preserves an existing primary cover.

Default settings are: online sources off, retain existing covers, combined request limit 12 per minute, and a 10-second timeout.

## Install and use

1. Extract `NumberCover-v0.1.2.zip` into one dedicated Number Cover folder under Jellyfin's `plugins` directory, then restart Jellyfin. Do not keep duplicate Number Cover DLLs in other plugin folders.
2. Open **Dashboard > Plugins > Number Cover** and enable online cover sources.
3. For one item, use **Edit images > Search**. For a batch, run a library scan or refresh.

Choose settings and sources only for content you are authorized to access, and comply with the source sites' terms.

## 一键更新所有封面

当前插件没有单独的“更新全部”按钮，但可以使用 Jellyfin 的媒体库批量刷新功能：

1. 打开 **控制台 > 插件 > Number Cover > 设置**，启用在线封面来源。
2. 如果需要覆盖已有封面，请关闭 **仅填充缺失封面（Only fill missing）**。
3. 打开 **控制台 > 媒体库**，找到需要处理的电影媒体库。
4. 选择 **刷新元数据**，使用“搜索缺失的元数据”模式，并勾选 **替换现有图片**。
5. 确认后等待任务完成。插件会逐部识别文件名中的番号，并按 Jable、DMM/FANZA 回退顺序查找封面。

默认请求上限为每分钟 12 次。大型媒体库需要较长时间，建议先在小型测试目录中确认效果。只有能够识别番号且在线来源返回有效图片的项目会更新。

## 项目说明与后续计划

这是我的第一个 AI 项目，已经由我亲自测试，当前版本可以正常运行。由于本阶段可用的 AI token 已经不足，下面这些功能需要等我有时间后继续开发：

1. 自动更新主演/演员姓名及相关信息。
2. 扩展更多番号和封面来源；目前部分 `ABP`、`EBOD` 等番号可能无法返回结果。
3. 完善文件名识别规则；目前某些连续书写、没有 `-` 分隔符的文件名无法自动搜索。
