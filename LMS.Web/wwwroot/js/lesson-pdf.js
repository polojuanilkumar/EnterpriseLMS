import { lessonMediaUrl } from "./lesson-media.js";

// The server proxy performs authorization and sets a sandbox CSP on the PDF.
export async function attachLessonPdf(frame, context) {
    if (!(frame instanceof HTMLIFrameElement)) throw new TypeError("An iframe is required.");
    frame.setAttribute("sandbox", "");
    frame.title = "Lesson presentation";
    frame.src = lessonMediaUrl(context);
    return () => frame.removeAttribute("src");
}
