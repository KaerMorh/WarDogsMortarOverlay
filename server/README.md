# WarDogs Go 房间服务

开发上下文：先读 ../docs/联机开发方案.md、联机协议.md、联机开发进度.md。

## 本地

Go 1.26+，当前验证使用项目 .tools/go（版本记录见进度）。

```sh
go test ./...
go run ./cmd/wardogs-server
```

默认监听 127.0.0.1:8080；客户端 ws://127.0.0.1:8080/ws；健康检查 GET /healthz。

Windows 项目根目录可单独构建服务端，不编译或改动 UI：

```powershell
./build-server.ps1
./build-server.ps1 -Linux
```

分别输出 `artifacts/multiplayer/wardogs-server.exe` 和 `artifacts/multiplayer/linux/wardogs-server`。Linux 二进制无需 .NET 或 Go 运行时，部署时设置可执行权限后启动。`wardogs-server healthcheck` 检查同一 WARDOGS_ADDR 的 `/readyz`，可供 Docker 使用。

## Docker

```sh
docker compose up -d --build
docker compose logs --tail 100
```

默认容器端口只绑定服务器本机，不直接公开明文 WebSocket。可用现有 HTTPS 反向代理转发至 127.0.0.1:8080。Caddy 站点例子（替换成自己的域名）：

```caddyfile
overlay.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

客户端填写 wss://overlay.example.com/ws。证书由反向代理管理，服务没有出站 HTTPS 依赖。需要开放 443；Caddy 自动签证还可能需要 80。服务器地址和域名尚未提供，当前不执行公网部署。

没有现有反向代理时，可使用附带的 Caddy Compose 扩展（域名先解析到本机，开放 80/443）：

```sh
export WARDOGS_DOMAIN=overlay.example.com
docker compose -f compose.yaml -f compose.public.yaml up -d --build
```

证书保存在 Docker 命名卷，常规更新不要删除卷。此扩展配置已编写，但当前开发机无 Docker，尚未执行容器启动验收。

容器内存限额 256MiB，Go 软内存目标 192MiB 是初始预算，并非承载承诺。可通过 compose 调整。在线上使用前进行人数/广播压力验证。

可配置环境变量：WARDOGS_ADDR、WARDOGS_MAX_ROOMS（64）、WARDOGS_MAX_MEMBERS（32）、WARDOGS_MAX_CONNECTIONS（512）。

配置错误会明确报错并退出，不静默使用默认值。当前房间成员上限允许配置 1–32，房间数 1–1024，总连接数 1–4096；增加总人数前应重新测量服务器资源。

## 服务端已实现的边界

- `/healthz` 用于存活检查，`/readyz` 在开始停机时变成 503。
- 入房超时 10 秒；心跳间隔 15 秒、超时 10 秒；写入超时 5 秒。
- 消息最大 8KiB，每连接 10 条/秒、突发 20 条；非法业务消息返回 error，不修改房间。
- 每连接最多排队 128 条、1MiB；全服务发送队列合计最多 32MiB。慢连接断开释放队列，不能无限消耗内存。
- 广播只编码一次，未变化的资料/炮位/目标仅 ACK，不重复广播；重命名只更新受影响成员。
- 停机主动关闭已升级的 WebSocket，并等待连接和队列回收；房间只存在于内存中。
- 每分钟输出聚合连接/房间/排队字节/消息计数，不记录房间码、UID、Callsign 或坐标。

`go test ./...` 包含配置、房间生命周期、并发入房发布、消息隔离、重复消息、时间边界、限流、超大消息、无心跳连接、排队预算、停机资源回收等测试。当前环境未运行 Linux 容器或 Go race detector，详见开发进度。

房间和 UID 仅用于同伴协作，不是账号认证。知道房间码即可加入，同 UID 会替换旧会话；不要公开发布实际房间码或身份文件。重启服务器清空房间，不删除客户端本次历史。
