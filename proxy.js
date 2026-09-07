// ap-proxy: local WebSocket relay for game clients whose bundled Mono/TLS
// stack can't negotiate TLS 1.2+ (e.g. old Unity mono.dll).
//
// The game connects to ws://127.0.0.1:<localPort> (no TLS, so the ancient
// Mono.Security stack never has to touch a modern handshake). This process
// makes the real wss:// connection to the Archipelago server using Node's
// TLS stack and pipes frames through unmodified in both directions.
//
// Usage:
//   node proxy.js <localPort> <remoteWssUrl>
//
// Example:
//   node proxy.js 38281 wss://archipelago.gg:38281

const WebSocket = require("ws");

const localPort = process.argv[2];
const remoteUrl = process.argv[3];

if (!localPort || !remoteUrl) {
  console.error("Usage: node proxy.js <localPort> <remoteWssUrl>");
  console.error("Example: node proxy.js 38281 wss://archipelago.gg:38281");
  process.exit(1);
}

const wss = new WebSocket.Server({ port: Number(localPort) }, () => {
  console.log(`Listening on ws://127.0.0.1:${localPort}`);
  console.log(`Relaying to ${remoteUrl}`);
});

wss.on("connection", (clientSocket) => {
  console.log("Game client connected.");

  // Queue frames that arrive from the game before the upstream connection
  // to the real server is open.
  const pending = [];
  let upstreamOpen = false;

  const upstream = new WebSocket(remoteUrl);

  upstream.on("open", () => {
    console.log("Upstream connection established.");
    upstreamOpen = true;
    for (const frame of pending) upstream.send(frame.data, { binary: frame.isBinary });
    pending.length = 0;
  });

  upstream.on("message", (data, isBinary) => {
    if (clientSocket.readyState === WebSocket.OPEN)
      clientSocket.send(data, { binary: isBinary });
  });

  upstream.on("close", (code, reason) => {
    console.log(`Upstream closed. Code: ${code} Reason: ${reason}`);
    if (clientSocket.readyState === WebSocket.OPEN) clientSocket.close(code);
  });

  upstream.on("error", (err) => {
    console.error("Upstream error: " + err.message);
  });

  clientSocket.on("message", (data, isBinary) => {
    if (upstreamOpen) upstream.send(data, { binary: isBinary });
    else pending.push({ data, isBinary });
  });

  clientSocket.on("close", (code, reason) => {
    console.log(`Game client disconnected. Code: ${code} Reason: ${reason}`);
    upstream.close();
  });

  clientSocket.on("error", (err) => {
    console.error("Game client socket error: " + err.message);
  });
});

wss.on("error", (err) => {
  console.error("Local server error: " + err.message);
});
