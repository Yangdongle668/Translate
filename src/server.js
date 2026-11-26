import { createHash, timingSafeEqual } from "node:crypto";
import { createServer } from "node:http";
import { ApiError } from "./api-error.js";
import { translateWithGoogle } from "./google.js";

const MAX_BODY_BYTES = 64 * 1024;
const LANGUAGE_CODE = /^[a-z]{2,3}(?:-[a-z0-9]{2,8}){0,2}$/i;
const utf8 = new TextDecoder("utf-8", { fatal: true });

function digest(token) {
  return createHash("sha256").update(token).digest();
}

function sendJson(response, status, body, headers = {}) {
  response.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": "no-store",
    ...headers,
  });
  response.end(JSON.stringify(body));
}

function requireMethod(request, method) {
  if (request.method !== method) {
    throw new ApiError(405, "METHOD_NOT_ALLOWED", `Use ${method} for this endpoint.`, { Allow: method });
  }
}

function bodyTooLarge() {
  return new ApiError(413, "BODY_TOO_LARGE", "Request body exceeds 64 KiB.");
}

async function readJson(request) {
  const mediaType = request.headers["content-type"]?.split(";", 1)[0].trim().toLowerCase();
  if (mediaType !== "application/json") {
    throw new ApiError(415, "UNSUPPORTED_MEDIA_TYPE", "Send a JSON request body.");
  }
  if (Number(request.headers["content-length"]) > MAX_BODY_BYTES) {
    throw bodyTooLarge();
  }

  let length = 0;
  const chunks = [];
  for await (const chunk of request.iterator({ destroyOnReturn: false })) {
    length += chunk.length;
    if (length > MAX_BODY_BYTES) throw bodyTooLarge();
    chunks.push(chunk);
  }

  try {
    return JSON.parse(utf8.decode(Buffer.concat(chunks, length)));
  } catch {
    throw new ApiError(400, "INVALID_JSON", "Send valid UTF-8 JSON.");
  }
}

function translationInput(body) {
  if (!body || typeof body !== "object" || Array.isArray(body)) {
    throw new ApiError(400, "INVALID_REQUEST", "The request body must be a JSON object.");
  }

  const { text, source = "auto", target } = body;
  if (typeof text !== "string" || !text.trim() || !text.isWellFormed()) {
    throw new ApiError(400, "INVALID_REQUEST", "Provide non-empty Unicode text.");
  }
  if (typeof source !== "string" || (source !== "auto" && !LANGUAGE_CODE.test(source))) {
    throw new ApiError(400, "INVALID_REQUEST", 'Provide a source language code or "auto".');
  }
  if (typeof target !== "string" || !LANGUAGE_CODE.test(target)) {
    throw new ApiError(400, "INVALID_REQUEST", "Provide a target language code such as zh-CN or en.");
  }
  return { text, source, target };
}

function createPublicRateLimiter() {
  const clientCounts = new Map();
  let requestCount = 0;
  let resetsAt = 0;

  return (address) => {
    const now = Date.now();
    if (now >= resetsAt) {
      clientCounts.clear();
      requestCount = 0;
      resetsAt = now + 60_000;
    }
    const clientCount = clientCounts.get(address) ?? 0;
    if (requestCount >= 60 || clientCount >= 30) {
      throw new ApiError(429, "SERVICE_RATE_LIMITED", "Too many requests for this server.", {
        "Retry-After": String(Math.ceil((resetsAt - now) / 1000)),
      });
    }
    clientCounts.set(address, clientCount + 1);
    requestCount++;
  };
}

export function createTranslationServer(apiToken, { allowAnonymous = false } = {}) {
  if (!apiToken && !allowAnonymous) throw new Error("A translation API token is required.");
  const expectedToken = apiToken ? digest(apiToken) : null;
  const limitRequest = expectedToken ? null : createPublicRateLimiter();

  return createServer({ requestTimeout: 15_000, headersTimeout: 10_000 }, async (request, response) => {
    try {
      const path = request.url.split("?", 1)[0];
      if (path === "/health") {
        requireMethod(request, "GET");
        sendJson(response, 200, { status: "ok" });
        return;
      }
      if (path !== "/v1/translate") {
        throw new ApiError(404, "NOT_FOUND", "Endpoint not found.");
      }
      requireMethod(request, "POST");

      if (expectedToken) {
        const match = /^Bearer +(\S+)$/i.exec(request.headers.authorization ?? "");
        if (!match || !timingSafeEqual(digest(match[1]), expectedToken)) {
          throw new ApiError(401, "UNAUTHORIZED", "A valid Bearer token is required.", { "WWW-Authenticate": "Bearer" });
        }
      }

      const input = translationInput(await readJson(request));
      limitRequest?.(request.socket.remoteAddress);
      const translation = await translateWithGoogle(input);
      sendJson(response, 200, translation);
    } catch (error) {
      if (response.destroyed) return;
      if (!(error instanceof ApiError)) {
        console.error("Unexpected error while handling an API request.");
        error = new ApiError(500, "INTERNAL_ERROR", "An unexpected error occurred.");
      }
      if (!request.complete) response.setHeader("Connection", "close");
      sendJson(response, error.status, { error: { code: error.code, message: error.message } }, error.headers);
    }
  });
}
