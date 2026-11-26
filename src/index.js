import { createTranslationServer } from "./server.js";

const apiToken = process.env.TRANSLATE_API_TOKEN ?? "";
const allowAnonymous = process.env.TRANSLATE_ALLOW_ANONYMOUS === "true";
const host = process.env.HOST || "127.0.0.1";
const port = Number(process.env.PORT ?? "3000");

if ((!apiToken && !allowAnonymous) || (apiToken && !/^[a-zA-Z0-9_-]{32,}$/.test(apiToken))) {
  console.error("Set TRANSLATE_API_TOKEN to at least 32 letters, digits, underscores, or hyphens.");
  process.exit(1);
}
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  console.error("Set PORT to an integer from 1 to 65535.");
  process.exit(1);
}

createTranslationServer(apiToken, { allowAnonymous }).listen(port, host, () => {
  console.log(`Translate API listening on ${host}:${port}`);
});
