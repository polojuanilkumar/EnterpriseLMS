// Optional enhancement for clients building lesson elements dynamically.
// Authentication uses the HttpOnly web cookie; never accept an API JWT here.
export function lessonMediaUrl({ organizationId, courseId, sectionId, lessonId, itemId }) {
    const ids = [organizationId, courseId, sectionId, lessonId, itemId];
    if (ids.some(id => typeof id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)))
        throw new TypeError("Valid lesson route IDs are required.");
    return `/learn/${organizationId}/courses/${courseId}/sections/${sectionId}/units/${lessonId}/items/${itemId}/media`;
}
export async function attachLessonMedia(element, context) {
    if (!(element instanceof HTMLMediaElement)) throw new TypeError("An audio or video element is required.");
    element.src = lessonMediaUrl(context);
    element.load();
    return () => {
        element.pause();
        element.removeAttribute("src");
        element.load();
    };
}
