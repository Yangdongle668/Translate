# Translate

Windows 便携翻译客户端和可部署在海外服务器上的 HTTP 服务。客户端通过服务器 IP 提交文字，服务器访问 Google 翻译的非官方端点。无需 Google Cloud API 密钥。 [上游调用示例](https://github.com/revesis/translate#why-this-works-without-a-browser)

## 在海外服务器一键部署

服务器需要安装 Git、Docker 和 Compose，并能访问 GitHub、Docker Hub 及 `translate.googleapis.com`。首次部署时执行以下命令。

```bash
git clone --depth 1 --branch v1.0.3 https://github.com/Yangdongle668/Translate.git && \
  docker compose -f Translate/compose.yaml up -d --build --wait
```

在云防火墙或安全组中允许客户端访问服务器的 TCP 3000 端口。仅供自己使用时，可以把来源限制为自己的公网 IP。

已下载源码时，可以在项目根目录运行 `docker compose up -d --build --wait`。

用以下命令在服务器上验证翻译。成功时会返回包含 `translatedText` 的 JSON。Google 可能对非官方端点限流。

```bash
curl -fsS http://127.0.0.1:3000/v1/translate \
  -H 'Content-Type: application/json' \
  --data '{"text":"Hello, world!","target":"zh-CN"}'
```

## Windows 客户端

1. 下载并运行 [Translate.exe](https://github.com/Yangdongle668/Translate/releases/latest/download/Translate.exe)。
2. 在「服务器地址」填入海外服务器的公网 IPv4 地址，例如 `203.0.113.10`。客户端默认使用 HTTP 和 3000 端口，会自动检查连接并记住可用地址。
3. 输入原文，点击「翻译」或按 Ctrl + Enter。默认自动检测源语言，翻译为简体中文。可用下拉菜单切换语言。

如需指定端口，填 `IP:端口`，例如 `203.0.113.10:8080`。已配置 HTTPS 反向代理时，填完整地址，例如 `https://translate.example.com`。

## 端口、HTTPS 与令牌

- Docker 默认在所有 IPv4 接口发布端口 3000。运行 Compose 时可设置 `PORT` 修改主机端口。
- 配置 HTTPS 反向代理时，可以设置 `TRANSLATE_BIND_ADDRESS=127.0.0.1`，再将代理的上游地址设为 `127.0.0.1:3000`。 [Caddy 反向代理文档](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)
- 默认允许 IP 直连，每个直连 IP 每分钟最多 30 个翻译请求，每个实例合计最多 60 个请求。只有能访问端口的客户端可以调用服务。
- 如需让第三方 API 客户端使用令牌，可在 `.env` 或环境变量中设置 `TRANSLATE_API_TOKEN`。令牌须包含至少 32 个字母、数字、下划线或连字符。启用该设置后，翻译请求必须携带 `Authorization: Bearer <令牌>`。Windows 客户端使用默认的免令牌模式。

## HTTP API

`GET /health` 检查服务进程，成功返回 `{"status":"ok"}`。

`POST /v1/translate` 接收 `Content-Type: application/json` 的 UTF-8 请求。

```json
{
  "text": "Hello, world!",
  "source": "auto",
  "target": "zh-CN"
}
```

`text` 为非空原文。`source` 可省略，默认为 `auto`。`target` 为目标语言代码，例如 `zh-CN`、`zh-TW`、`en` 或 `ja`。

```json
{
  "translatedText": "你好，世界！",
  "source": "en",
  "target": "zh-CN"
}
```

响应中的 `source` 为指定或检测到的源语言。自动检测结果缺失时为 `null`。请求体上限为 64 KiB，编码后的 Google 请求 URL 上限为 6000 字节。错误响应包含 `error.code` 和 `error.message`。

| HTTP | 错误代码 | 含义 |
| --- | --- | --- |
| 400 | `INVALID_JSON`、`INVALID_REQUEST`、`UPSTREAM_REJECTED_REQUEST` | 检查 JSON、文本或语言代码 |
| 401 | `UNAUTHORIZED` | 检查访问令牌 |
| 413 | `BODY_TOO_LARGE`、`TEXT_TOO_LONG` | 缩短文本再提交 |
| 415 | `UNSUPPORTED_MEDIA_TYPE` | 使用 `application/json` |
| 429 | `SERVICE_RATE_LIMITED`、`UPSTREAM_RATE_LIMITED` | 服务或 Google 限流。收到 `Retry-After` 时按该值等待 |
| 502 | `UPSTREAM_ERROR` | Google 连接失败或响应不可用 |
| 504 | `UPSTREAM_TIMEOUT` | Google 在 8 秒内未完成响应 |

## 从源代码构建

Windows 10/11 客户端使用 Windows 自带的 .NET Framework 4.8 或更高版本。运行以下 PowerShell 命令生成 `client/dist/Translate.exe`。 [Microsoft 的 Windows 系统要求](https://learn.microsoft.com/dotnet/framework/get-started/system-requirements)

```powershell
.\client\build.ps1
```

本地开发服务需要 Node.js 24 或更高版本。以下命令在 `127.0.0.1:3000` 启动服务。

```powershell
$env:TRANSLATE_ALLOW_ANONYMOUS = 'true'
npm.cmd start
```
