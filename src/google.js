import { ApiError } from "./api-error.js";

const GOOGLE_ENDPOINT = "https://translate.googleapis.com/translate_a/t";
const MAX_GOOGLE_URL_LENGTH = 6_000;
const GOOGLE_TIMEOUT_MS = 8_000;

export async function translateWithGoogle({ text, source, target }) {
  const url = new URL(GOOGLE_ENDPOINT);
  url.search = new URLSearchParams({
    client: "dict-chrome-ex",
    sl: source,
    tl: target,
    q: text,
  }).toString();

  if (url.href.length > MAX_GOOGLE_URL_LENGTH) {
    throw new ApiError(413, "TEXT_TOO_LONG", "Split the text into shorter passages.");
  }

  const signal = AbortSignal.timeout(GOOGLE_TIMEOUT_MS);
  let payload;
  try {
    const response = await fetch(url, {
      headers: { Accept: "application/json" },
      redirect: "manual",
      signal,
    });

    if (!response.ok) {
      await response.body?.cancel();
      if (response.status === 429) {
        const retryAfter = response.headers.get("retry-after");
        throw new ApiError(
          429,
          "UPSTREAM_RATE_LIMITED",
          "Google Translate is rate limiting requests.",
          retryAfter ? { "Retry-After": retryAfter } : {},
        );
      }
      if (response.status === 400) {
        throw new ApiError(400, "UPSTREAM_REJECTED_REQUEST", "Google Translate rejected the text or language codes.");
      }
      if (response.status === 414) {
        throw new ApiError(413, "TEXT_TOO_LONG", "Split the text into shorter passages.");
      }
      throw new Error(`Google Translate returned HTTP ${response.status}.`);
    }

    payload = await response.json();
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (signal.aborted) {
      throw new ApiError(504, "UPSTREAM_TIMEOUT", "Google Translate timed out.");
    }
    throw new ApiError(502, "UPSTREAM_ERROR", "Could not get a response from Google Translate.");
  }

  // With one q parameter, Google returns one string or a [translation, language] pair.
  const entry = Array.isArray(payload) && payload.length === 1 ? payload[0] : null;
  const [translatedText, detectedLanguage] = Array.isArray(entry) ? entry : [entry, null];
  if (typeof translatedText !== "string" || !translatedText.trim()) {
    throw new ApiError(502, "UPSTREAM_ERROR", "Google Translate returned an invalid translation.");
  }

  const detectedSource = typeof detectedLanguage === "string" && detectedLanguage ? detectedLanguage : null;
  return {
    translatedText,
    source: source === "auto" ? detectedSource : source,
    target,
  };
}
