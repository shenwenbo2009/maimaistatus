namespace MaimaiStatus.Web.Components.Shared;

/// <summary>文档参数行。</summary>
public sealed record DocParam(string Name, string Pos, string Required, string Type, string Desc);

/// <summary>文档字段行。</summary>
public sealed record DocField(string Name, string Type, string Desc);

/// <summary>接口卡(方法/路径/用途/参数/五语言示例/字段表/响应例)。</summary>
public sealed record DocEndpoint(
    string Id, string Method, string Path, string Summary, string Purpose,
    IReadOnlyList<DocParam> Params,
    IReadOnlyDictionary<string, string> Codes,
    IReadOnlyList<DocField> Fields,
    string Resp);

/// <summary>接口分组(锚点段)。</summary>
public sealed record DocGroup(string Id, string Title, IReadOnlyList<DocEndpoint> Items);

/// <summary>
/// 开发者文档静态目录(内容与旧 frontend/pages/docs.vue 逐条一致,单源维护)。
/// 使用例语言模板由 BuildCodes 生成,与旧 getCodes() 同构。
/// </summary>
public static class DocsCatalog
{
    public const string BaseUrl = "https://maimai.imikufans.cn/api";

    public static readonly (string Key, string Label)[] Languages =
    [
        ("curl", "cURL"),
        ("go", "Go"),
        ("rust", "Rust"),
        ("js", "JavaScript"),
        ("py", "Python"),
    ];

    private static readonly DocParam[] NoParams = [];

    private static Dictionary<string, string> GetCodes(string path, string extra = "")
    {
        var url = $"{BaseUrl}{path}{extra}";
        return new()
        {
            ["curl"] = $"curl \"{url}\"",
            ["go"] =
                "package main\n\nimport (\n\t\"fmt\"\n\t\"io\"\n\t\"log\"\n\t\"net/http\"\n)\n\nfunc main() {\n\tresp, err := http.Get(\"" + url + "\")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tbody, _ := io.ReadAll(resp.Body)\n\tfmt.Println(string(body))\n}",
            ["rust"] =
                "#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet body = reqwest::get(\"" + url + "\").await?.text().await?;\n\tprintln!(\"{body}\");\n\tOk(())\n}",
            ["js"] = $"const r = await fetch('{url}');\nconst d = await r.json();\nconsole.log(d);",
            ["py"] = $"import requests\n\nd = requests.get('{url}', timeout=10).json()\nprint(d)",
        };
    }

    /// <summary>SSE 专用示例(五语言,流式读取)。</summary>
    private static Dictionary<string, string> StreamCodes()
    {
        var b = BaseUrl;
        return new()
        {
            ["curl"] = $"curl -N \"{b}/stream\"",
            ["go"] =
                "package main\n\nimport (\n\t\"bufio\"\n\t\"fmt\"\n\t\"log\"\n\t\"net/http\"\n)\n\nfunc main() {\n\tresp, err := http.Get(\"" + b + "/stream\")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tsc := bufio.NewScanner(resp.Body)\n\tfor sc.Scan() {\n\t\tfmt.Println(sc.Text())\n\t}\n}",
            ["rust"] =
                "use futures_util::StreamExt;\n\n#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet mut s = reqwest::get(\"" + b + "/stream\").await?.bytes_stream();\n\twhile let Some(chunk) = s.next().await {\n\t\tprint!(\"{}\", String::from_utf8_lossy(&chunk?));\n\t}\n\tOk(())\n}",
            ["js"] = $"const es = new EventSource('{b}/stream');\nes.addEventListener('status', e => console.log(JSON.parse(e.data)));\nes.onerror = () => console.warn('stream disconnected');",
            ["py"] = "import requests\n\nwith requests.get('" + b + "/stream', stream=True, timeout=65) as r:\n    for line in r.iter_lines(decode_unicode=True):\n        if line.startswith('data:'):\n            print(line[5:].strip())",
        };
    }

    /// <summary>上报 POST 专用示例(五语言,含签名)。</summary>
    private static Dictionary<string, string> ReportCodes()
    {
        var b = BaseUrl;
        return new()
        {
            ["curl"] =
                "# 1. 领码(记下 captcha_id 与 code,code 给用户照打)\ncurl \"" + b + "/report/captcha\"\n\n# 2. 上报(先本地算好 bodyHash 与签名,见 Go 例)\nBODY='[{\"t\":103,\"v\":1,\"r\":\"WEB\",\"bot_display_name\":\"路人\"}]'\ncurl -X POST \"" + b + "/report\" \\\n  -H 'Content-Type: application/json' \\\n  -H 'X-Client-ID: <your-key>' \\\n  -H \"X-Timestamp: $(date +%s%3N)\" \\\n  -H 'X-Signature: <hmac-sha256-hex>' \\\n  -H 'X-Captcha-ID: <captcha_id>' \\\n  -H 'X-Captcha-Code: <用户照打的8位>' \\\n  -d \"$BODY\"",
            ["go"] =
                "package main\n\nimport (\n\t\"crypto/hmac\"\n\t\"crypto/sha256\"\n\t\"encoding/hex\"\n\t\"fmt\"\n\t\"io\"\n\t\"log\"\n\t\"net/http\"\n\t\"strings\"\n\t\"time\"\n)\n\nfunc main() {\n\tclientID, privKey := \"<your-key>\", \"<your-key>\"\n\tbody := `[{\"t\":103,\"v\":1,\"r\":\"WEB\"}]`\n\tts := fmt.Sprint(time.Now().UnixMilli())\n\tbh := sha256.Sum256([]byte(body))\n\tmac := hmac.New(sha256.New, []byte(privKey))\n\tmac.Write([]byte(\"POST:/bot-post:\" + clientID + \":\" + ts + \":\" + hex.EncodeToString(bh[:])))\n\treq, _ := http.NewRequest(\"POST\", \"" + b + "/report\", strings.NewReader(body))\n\treq.Header.Set(\"Content-Type\", \"application/json\")\n\treq.Header.Set(\"X-Client-ID\", clientID)\n\treq.Header.Set(\"X-Timestamp\", ts)\n\treq.Header.Set(\"X-Signature\", hex.EncodeToString(mac.Sum(nil)))\n\treq.Header.Set(\"X-Captcha-ID\", \"<captcha_id>\")\n\treq.Header.Set(\"X-Captcha-Code\", \"<用户照打的8位>\")\n\tresp, err := http.DefaultClient.Do(req)\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\trb, _ := io.ReadAll(resp.Body)\n\tfmt.Println(resp.StatusCode, string(rb))\n}",
            ["rust"] =
                "use hmac::{Hmac, Mac};\nuse sha2::Sha256;\n\n#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet (client_id, priv_key) = (\"<your-key>\", \"<your-key>\");\n\tlet body = r#\"[{\"t\":103,\"v\":1,\"r\":\"WEB\"}]\"#;\n\tlet ts = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH)?.as_millis().to_string();\n\tlet body_hash = hex::encode(sha2::Sha256::digest(body.as_bytes()));\n\tlet mut mac = Hmac::<Sha256>::new_from_slice(priv_key.as_bytes())?;\n\tmac.update(format!(\"POST:/bot-post:{client_id}:{ts}:{body_hash}\").as_bytes());\n\tlet sig = hex::encode(mac.finalize().into_bytes());\n\tlet res = reqwest::Client::new().post(\"" + b + "/report\")\n\t\t.header(\"Content-Type\", \"application/json\")\n\t\t.header(\"X-Client-ID\", client_id).header(\"X-Timestamp\", ts).header(\"X-Signature\", sig)\n\t\t.header(\"X-Captcha-ID\", \"<captcha_id>\").header(\"X-Captcha-Code\", \"<用户照打的8位>\")\n\t\t.body(body.to_string()).send().await?;\n\tprintln!(\"{} {}\", res.status(), res.text().await?);\n\tOk(())\n}",
            ["js"] =
                "const clientId = '<your-key>', privKey = '<your-key>';\nconst body = JSON.stringify([{ t: 103, v: 1, r: 'WEB' }]);\nconst ts = String(Date.now());\nconst buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(body));\nconst bodyHash = [...new Uint8Array(buf)].map(b => b.toString(16).padStart(2, '0')).join('');\nconst key = await crypto.subtle.importKey('raw', new TextEncoder().encode(privKey), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);\nconst sigBuf = await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(`POST:/bot-post:${clientId}:${ts}:${bodyHash}`));\nconst sig = [...new Uint8Array(sigBuf)].map(b => b.toString(16).padStart(2, '0')).join('');\nconst r = await fetch('" + b + "/report', {\n  method: 'POST',\n  headers: { 'Content-Type': 'application/json', 'X-Client-ID': clientId, 'X-Timestamp': ts, 'X-Signature': sig, 'X-Captcha-ID': '<captcha_id>', 'X-Captcha-Code': '<用户照打的8位>' },\n  body\n});\nconsole.log(r.status, await r.text());",
            ["py"] =
                "import hashlib, hmac, json, time\nimport requests\n\nclient_id, priv_key = '<your-key>', '<your-key>'\nbody = json.dumps([{'t': 103, 'v': 1, 'r': 'WEB'}], separators=(',', ':'))\nts = str(int(time.time() * 1000))\nbody_hash = hashlib.sha256(body.encode()).hexdigest()\nsig = hmac.new(priv_key.encode(), f'POST:/bot-post:{client_id}:{ts}:{body_hash}'.encode(), hashlib.sha256).hexdigest()\nr = requests.post('" + b + "/report', data=body, headers={\n    'Content-Type': 'application/json', 'X-Client-ID': client_id,\n    'X-Timestamp': ts, 'X-Signature': sig,\n    'X-Captcha-ID': '<captcha_id>', 'X-Captcha-Code': '<用户照打的8位>',\n}, timeout=10)\nprint(r.status_code, r.text)",
        };
    }

    /// <summary>healthz 专用示例(生产域名,不带 /api)。</summary>
    private static Dictionary<string, string> HealthzCodes()
    {
        const string url = "https://maimai.imikufans.cn/api/healthz";
        return new()
        {
            ["curl"] = $"curl \"{url}\"",
            ["go"] =
                "package main\n\nimport (\n\t\"fmt\"\n\t\"io\"\n\t\"log\"\n\t\"net/http\"\n)\n\nfunc main() {\n\tresp, err := http.Get(\"" + url + "\")\n\tif err != nil {\n\t\tlog.Fatal(err)\n\t}\n\tdefer resp.Body.Close()\n\tbody, _ := io.ReadAll(resp.Body)\n\tfmt.Println(string(body))\n}",
            ["rust"] =
                "#[tokio::main]\nasync fn main() -> Result<(), Box<dyn std::error::Error>> {\n\tlet body = reqwest::get(\"" + url + "\").await?.text().await?;\n\tprintln!(\"{body}\");\n\tOk(())\n}",
            ["js"] = $"const r = await fetch('{url}');\nconsole.log(await r.json());",
            ["py"] = $"import requests\n\nprint(requests.get('{url}', timeout=10).json())",
        };
    }

    public static readonly IReadOnlyList<DocGroup> Groups =
    [
        new("status", "实时状态",
        [
            new DocEndpoint(
                "status", "GET", "/api/status", "实时状态(代理上游 /api/bot,20s 缓存)",
                "查询舞萌DX 服务器实时状态与延迟,是监控页的核心数据源。与上游 /api/bot 同结构,命中缓存带 X-Cache: HIT。",
                NoParams, GetCodes("/status"),
                [
                    new("version", "string", "上游数据版本"),
                    new("timestamp", "string", "上游数据时间"),
                    new("status / status_text", "string", "兼容旧字段:normal/anomaly/empty/maintenance"),
                    new("incident_level", "string", "兼容旧字段:normal/degraded/outage/maintenance"),
                    new("verdict", "string", "总判定:normal/recovering/degraded/outage/maintenance/nodata"),
                    new("verdict_text", "string", "总判定文案"),
                    new("services[]", "array", "六个探针:{key,state,state_text,latency,duration_text},key=net/aime/title/member/qr/bl"),
                    new("summary", "string", "摘要,如\"服务器运行正常,5条异常,17条正常上报\""),
                    new("latency", "object", "{current_ms,load_text,volatility_text}"),
                    new("reports", "object", "{anomaly_count,normal_count}:近1小时异常/正常上报数"),
                    new("broadcast", "object|null", "{msg}:公告,为空忽略"),
                ],
                "{\n  \"version\": \"26.10\",\n  \"status\": \"normal\",\n  \"verdict\": \"recovering\",\n  \"verdict_text\": \"部分服务性能恢复\",\n  \"services\": [\n    { \"key\": \"net\", \"name\": \"NET\", \"state\": \"ok\", \"state_text\": \"正常\", \"latency\": 98, \"duration_text\": \"\" },\n    { \"key\": \"member\", \"name\": \"公众号(NET)\", \"state\": \"recovering\", \"state_text\": \"性能恢复\", \"latency\": 318, \"duration_text\": \"2 分钟前恢复\" }\n  ],\n  \"summary\": \"服务器运行正常，5条异常，17条正常上报\",\n  \"latency\": { \"current_ms\": 25, \"load_text\": \"流畅\", \"volatility_text\": \"波动大\" },\n  \"reports\": { \"anomaly_count\": 5, \"normal_count\": 17 },\n  \"broadcast\": null\n}"),
            new DocEndpoint(
                "bot", "GET", "/api/bot", "兼容原路径,同 /api/status",
                "为兼容直连上游的老客户端保留的别名,与 /api/status 同缓存同结构,新接入请用 /api/status。",
                NoParams, GetCodes("/bot"),
                [new("(全部)", "-", "同 /api/status")],
                "同 /api/status(结构完全一致)。"),
        ]),
        new("snapshot", "静态快照(1h 缓存)",
        [
            new DocEndpoint(
                "top100", "GET", "/api/top100", "全服游玩 Top100",
                "按全服游玩次数排序的乐曲榜,供全服乐曲页渲染。封面为相对路径,需拼 https://mai.chongxi.us 前缀。",
                NoParams, GetCodes("/top100"),
                [
                    new("totalPlays", "int", "全服总游玩次数"),
                    new("songs[]", "array", "按名次排列:{id,name,plays,cover}"),
                ],
                "{\n  \"totalPlays\": 481002,\n  \"songs\": [\n    { \"id\": 731, \"name\": \"妄想感傷代償連盟\", \"plays\": 318703, \"cover\": \"/top100-covers/731.webp\" },\n    { \"id\": 11558, \"name\": \"神っぽいな\", \"plays\": 294880, \"cover\": \"/top100-covers/11558.webp\" }\n  ]\n}"),
            new DocEndpoint(
                "chart145", "GET", "/api/chart145", "定数 14.5–15.0 谱面排行",
                "越级排行数据源。注意上游 level 低标了一档(3≈真 MAS,4≈真 ReM),本站已用落雪 level_value 校准,见越级页 TIER_FIX。",
                NoParams, GetCodes("/chart145"),
                [
                    new("totalPlayers", "int", "合计游玩人次"),
                    new("songs[]", "array", "{rank,players,musicId,name,level,rate(定数),avg,med,mode,cover}"),
                ],
                "{\n  \"totalPlayers\": 4760955,\n  \"songs\": [\n    { \"rank\": 1, \"players\": 92360, \"musicId\": 834, \"name\": \"PANDORA PARADOXXX\", \"level\": 4, \"rate\": 15, \"avg\": 77.1, \"med\": 83.3, \"mode\": 101, \"cover\": \"/chart145-covers/834.webp\" },\n    { \"rank\": 2, \"players\": 66315, \"musicId\": 363, \"name\": \"Oshama Scramble!\", \"level\": 3, \"rate\": 14.5, \"avg\": 93.4, \"med\": 98, \"mode\": 100.6, \"cover\": \"/chart145-covers/363.webp\" }\n  ]\n}"),
            new DocEndpoint(
                "ratinglist", "GET", "/api/ratinglist", "Rating 分布",
                "全服 Rating 百分一段位分布。不带 version 返回全版本,带则返回单版本(version 仅允许 dx2023/2024/2025/2026,非法返回 400)。",
                [new("version", "query", "否", "string", "dx2023/dx2024/dx2025/dx2026,缺省返回全版本")],
                GetCodes("/ratinglist", "?version=dx2026"),
                [
                    new("versions[]", "array", "版本清单:{key,label,total}"),
                    new("data[]", "array", "各版本:{key,label,total,data[{bucket,count}]}(每 100 分一段)"),
                ],
                "{\n  \"versions\": [{ \"key\": \"dx2026\", \"label\": \"DX2026\", \"total\": 283092 }],\n  \"data\": [{\n    \"key\": \"dx2026\", \"label\": \"DX2026\", \"total\": 283092,\n    \"data\": [{ \"bucket\": 100, \"count\": 10255 }, { \"bucket\": 200, \"count\": 8989 }]\n  }]\n}"),
        ]),
        new("history", "本地历史(网关采集)",
        [
            new DocEndpoint(
                "history", "GET", "/api/history", "点位列表,最新在前",
                "网关每 30 秒采集上游落盘的本地历史,历史页与图表的数据来源。网关未启动时无数据(无法兜底上游)。",
                [new("limit", "query", "否", "int", "默认 288,最大 2000")],
                GetCodes("/history", "?limit=2"),
                [
                    new("points[]", "array", "最新在前:{ts(毫秒),verdict,verdict_text,latency_ms,anomaly,normal,ok_count,down_count,maint,services[{key,state,latency_ms}]}"),
                    new("maint", "bool", "维护点:上游报 maintenance 或落在常规窗口(默认北京时间每日 04:00-07:00)"),
                ],
                "{\n  \"points\": [{\n    \"ts\": 1791718786836, \"verdict\": \"recovering\", \"verdict_text\": \"部分服务性能恢复\",\n    \"latency_ms\": 25, \"anomaly\": 5, \"normal\": 17,\n    \"ok_count\": 5, \"down_count\": 0, \"maint\": false,\n    \"services\": [{ \"key\": \"net\", \"state\": \"ok\", \"latency_ms\": 98 }]\n  }]\n}"),
            new DocEndpoint(
                "history-summary", "GET", "/api/history/summary", "近 12h 可用率与平均延迟",
                "历史页仪表盘数字。uptime_pct 与 availability 全服务检查数口径同源(维护点位剔除)。",
                NoParams, GetCodes("/history/summary"),
                [
                    new("total", "int", "非维护采集点数"),
                    new("uptime_pct", "string", "可用率百分比(两位小数)"),
                    new("avg_latency_ms", "int", "平均延迟"),
                    new("latest", "object", "{ts,verdict,verdict_text}"),
                    new("maint_points", "int", "维护点数"),
                    new("window_hours", "int", "固定 12"),
                ],
                "{\n  \"avg_latency_ms\": 40,\n  \"latest\": { \"ts\": 1791718786836, \"verdict\": \"recovering\", \"verdict_text\": \"部分服务性能恢复\" },\n  \"maint_points\": 0, \"total\": 451, \"uptime_pct\": \"82.67\", \"window_hours\": 12\n}"),
            new DocEndpoint(
                "history-range", "GET", "/api/history/range", "按毫秒时间戳范围查历史",
                "按时间窗口过滤历史点位,供自定义范围查询。",
                [
                    new("from", "query", "否", "int", "起始毫秒时间戳"),
                    new("to", "query", "否", "int", "结束毫秒时间戳"),
                    new("limit", "query", "否", "int", "默认 2000,最大 5000"),
                ],
                GetCodes("/history/range", "?from=1791718000000&limit=1"),
                [new("points[]", "array", "同 /api/history 点位结构")],
                "{\n  \"points\": [{\n    \"ts\": 1791718786836, \"verdict\": \"recovering\", \"verdict_text\": \"部分服务性能恢复\",\n    \"latency_ms\": 25, \"anomaly\": 5, \"normal\": 17, \"maint\": false\n  }]\n}"),
        ]),
        new("stats", "统计聚合(本地计算)",
        [
            new DocEndpoint(
                "incidents", "GET", "/api/incidents", "故障事件列表",
                "verdict 非 normal 的连续点位自动提取为一段事件,最新在前。",
                NoParams, GetCodes("/incidents"),
                [
                    new("incidents[]", "array", "{start_ts,end_ts,duration_sec(秒),worst_verdict,verdict_text,ongoing}"),
                    new("count", "int", "事件数"),
                ],
                "{\n  \"count\": 3,\n  \"incidents\": [{\n    \"duration_sec\": 94, \"end_ts\": 1791718786836, \"ongoing\": true,\n    \"start_ts\": 1791718697190, \"verdict_text\": \"部分服务性能恢复\", \"worst_verdict\": \"degraded\"\n  }]\n}"),
            new DocEndpoint(
                "latency-stats", "GET", "/api/latency/stats", "延迟分位统计",
                "指定窗口内的延迟分布,维护点位剔除。",
                [new("hours", "query", "否", "int", "默认 24,最大 168")],
                GetCodes("/latency/stats", "?hours=24"),
                [
                    new("hours / count", "int", "窗口小时数 / 样本数"),
                    new("min / p50 / p90 / p99 / max / avg / current", "int", "毫秒"),
                ],
                "{ \"avg\": 40, \"count\": 451, \"current\": 25, \"hours\": 24, \"max\": 1955, \"min\": 25, \"p50\": 28, \"p90\": 31, \"p99\": 291 }"),
            new DocEndpoint(
                "reports-trend", "GET", "/api/reports/trend", "上报趋势(正序)",
                "异常/正常上报数随时间序列,正序返回可直接画线,也是图表概览上报档的数据源。",
                [new("limit", "query", "否", "int", "默认 288,最大 2000")],
                GetCodes("/reports/trend", "?limit=2"),
                [new("trend[]", "array", "{ts,anomaly,normal}")],
                "{ \"trend\": [{ \"anomaly\": 5, \"normal\": 17, \"ts\": 1791718756898 }] }"),
            new DocEndpoint(
                "services-availability", "GET", "/api/services/availability", "各服务可用率",
                "服务可用性柱状图 + 六宫格的数据源。maint 柱前端画满格灰;overall_pct 与 history/summary 的 uptime_pct 同值。",
                [new("hours", "query", "否", "int", "默认 12,最大 168")],
                GetCodes("/services/availability", "?hours=12"),
                [
                    new("hours", "int", "窗口小时数"),
                    new("buckets[]", "array", "48 柱:{ts,ratio,maint}(maint 柱置灰)"),
                    new("services[]", "array", "{key,name,availability_pct,avg_latency_ms,checks}"),
                    new("overall_pct", "string", "全服务总可用率(与 summary 同源)"),
                ],
                "{\n  \"hours\": 12, \"overall_pct\": \"82.67\",\n  \"buckets\": [{ \"maint\": false, \"ratio\": 1, \"ts\": 1791675599081 }],\n  \"services\": [{ \"availability_pct\": \"93.8\", \"avg_latency_ms\": 114, \"checks\": 451, \"key\": \"net\", \"name\": \"NET\" }]\n}"),
            new DocEndpoint(
                "services-latency", "GET", "/api/services/latency", "各服务延迟序列",
                "图表概览延迟档的多线数据源,自动降采样。maint_spans 为维护区间,前端压 0 灰线。",
                [
                    new("hours", "query", "否", "int", "默认 24,最大 168"),
                    new("maxpoints", "query", "否", "int", "默认 240,最大 1000"),
                ],
                GetCodes("/services/latency", "?hours=24&maxpoints=3"),
                [
                    new("hours", "int", "窗口小时数"),
                    new("services[]", "array", "{key,name,series[[ts,ms]]}"),
                    new("maint_spans", "array|null", "维护区间 [[start,end],...](毫秒,无则 null)"),
                ],
                "{\n  \"hours\": 24, \"maint_spans\": null,\n  \"services\": [{ \"key\": \"net\", \"name\": \"NET\", \"series\": [[1791705194636, 108]] }]\n}"),
            new DocEndpoint(
                "overview", "GET", "/api/overview", "聚合看板",
                "一次请求拿到状态原字段 + history_summary + latency_stats + ongoing_incidents,首页可用它一次渲染(本站目前为分开请求,效果等价)。",
                NoParams, GetCodes("/overview"),
                [
                    new("(状态原字段)", "-", "同 /api/status"),
                    new("history_summary", "object", "同 /api/history/summary"),
                    new("latency_stats", "object", "同 /api/latency/stats(24h)"),
                    new("ongoing_incidents", "array", "进行中的事件(同 incidents 项结构)"),
                ],
                "{\n  \"verdict\": \"recovering\", \"verdict_text\": \"部分服务性能恢复\",\n  \"history_summary\": { \"uptime_pct\": \"82.67\", \"avg_latency_ms\": 40 },\n  \"latency_stats\": { \"p50\": 28, \"p99\": 291 },\n  \"ongoing_incidents\": [{ \"ongoing\": true, \"worst_verdict\": \"degraded\" }]\n}"),
        ]),
        new("stream", "实时推送",
        [
            new DocEndpoint(
                "stream", "GET", "/api/stream", "SSE 实时推送",
                "订阅服务器状态实时流。连接后先吐一次当前状态,之后每次采集(约 30s)广播 event: status,心跳 : ping 每 20s。断线重连即可,注意反代须 proxy_buffering off。",
                NoParams, StreamCodes(),
                [
                    new("event: status", "SSE", "data 为 JSON(同 /api/status 结构)"),
                    new(": ping", "SSE", "每 20s 心跳"),
                ],
                "event: status\ndata: {\"verdict\":\"recovering\",\"verdict_text\":\"部分服务性能恢复\", ...}\n\n: ping"),
        ]),
        new("report", "手动上报",
        [
            new DocEndpoint(
                "report-captcha", "GET", "/api/report/captcha", "领取上报验证码",
                "上报前先领取 8 位数字验证码(后端随机生成,5 分钟有效,一次有效),上报时原样带回 X-Captcha-ID / X-Captcha-Code。",
                NoParams, GetCodes("/report/captcha"),
                [
                    new("captcha_id", "string", "验证码 ID"),
                    new("code", "string", "8 位数字验证码(展示给用户照打)"),
                    new("expires_in", "int", "有效秒数:300"),
                ],
                "{ \"captcha_id\": \"103d1d0fff6bcb7b72b07d29131af067\", \"code\": \"09584263\", \"expires_in\": 300 }"),
            new DocEndpoint(
                "report", "POST", "/api/report", "上报透传到上游 bot-post",
                "机台异常手动上报。Body 为上报对象数组,签名算法见上游文档(先 SHA-256 取 bodyHash,再 HMAC-SHA256 对 \"POST:/bot-post:{clientId}:{ts}:{bodyHash}\" 签名,十六进制)。网关验本地验证码后透传上游,成功 200(透传上游原样),401 为上游鉴权失败,429 限流。建议 30s 聚合一次。类型代码 t:101 机台断网 / 102 无法登录 / 103 NET 打不开 / 104 二维码 / 105 成绩上传失败 / 202 小黑屋 / 300 排队时长(v 为秒) / 501 恢复正常。",
                [
                    new("X-Client-ID", "header", "是", "string", "自定义 ID(英文字母+数字),私钥发邮件至 qwq@chongxi.us 获取"),
                    new("X-Timestamp", "header", "是", "string", "毫秒时间戳字符串"),
                    new("X-Signature", "header", "是", "string", "HMAC-SHA256 十六进制摘要"),
                    new("X-Captcha-ID", "header", "是", "string", "验证码 ID(先调 captcha 接口)"),
                    new("X-Captcha-Code", "header", "是", "string", "用户照打的 8 位数字"),
                    new("body", "body", "是", "array", "[{t,v,r,bot_display_name?}],r 建议 \"WEB\",缺验证码返回 400,错/过期返回 403"),
                ],
                ReportCodes(),
                [
                    new("200", "status", "已受理(透传上游原样返回)"),
                    new("400", "status", "缺头或 Body 非数组,如 {\"error\":\"missing X-Captcha-ID / X-Captcha-Code\"}"),
                    new("403", "status", "验证码无效/过期:{\"error\":\"invalid or expired captcha\"}"),
                    new("401", "status", "上游鉴权失败(检查 Client ID / Private Key)"),
                    new("429", "status", "被限流,30 秒后再试"),
                ],
                "HTTP 200(透传上游原样)\n\n验证码错误时:\n{\"error\":\"invalid or expired captcha\"}"),
        ]),
        new("lxns", "落雪曲目",
        [
            new DocEndpoint(
                "lxns-songs", "GET", "/api/lxns/songs", "公开曲目列表(1h 缓存)",
                "落雪曲库全量(含各难度定数 level_value),本站用它校准越级页难度档,无需登录。",
                NoParams, GetCodes("/lxns/songs"),
                [
                    new("songs[]", "array", "{id,title,artist,genre,bpm,version,difficulties{standard[],dx[]}}"),
                    new("difficulties[]", "array", "{type,difficulty(0-4=BAS/ADV/EXP/MAS/ReM),level,level_value(定数),note_designer,version}"),
                    new("genres / versions", "array", "分类与版本表"),
                ],
                "{\n  \"songs\": [{\n    \"id\": 8, \"title\": \"True Love Song\", \"bpm\": 150, \"version\": 10000,\n    \"difficulties\": { \"standard\": [\n      { \"type\": \"standard\", \"difficulty\": 3, \"level\": \"12\", \"level_value\": 12.4 }\n    ], \"dx\": [] }\n  }]\n}"),
        ]),
        new("ops", "运维",
        [
            new DocEndpoint(
                "healthz", "GET", "/api/healthz", "健康检查(同域反代口径)",
                "网关存活探针,部署验证用。网关原生在域名根下 /healthz,为适配同域反代只放行 /api/* 的场景,加了 /api/healthz 别名,两者等价。",
                NoParams, HealthzCodes(),
                [
                    new("ok", "bool", "存活"),
                    new("time", "string", "网关时间(RFC3339)"),
                ],
                "{ \"ok\": true, \"time\": \"2026-10-11T19:39:51+08:00\" }"),
            new DocEndpoint(
                "openapi", "GET", "/api/openapi.json", "OpenAPI 3.0 规格",
                "机器可读的接口规格(本页即其人工版),可导入 Postman / 代码生成。",
                NoParams, GetCodes("/openapi.json"),
                [
                    new("openapi", "string", "固定 3.0.3"),
                    new("info / paths", "object", "标题版本与路径表"),
                ],
                "{\n  \"openapi\": \"3.0.3\",\n  \"info\": { \"title\": \"Maimai Status Gateway\", \"version\": \"1.1.0\" },\n  \"paths\": { \"/healthz\": { ... }, \"/api/status\": { ... } }\n}"),
        ]),
    ];
}
