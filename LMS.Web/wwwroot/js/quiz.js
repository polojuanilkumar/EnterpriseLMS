"use strict";
document.querySelectorAll("form[data-quiz-submit]").forEach(form => {
    let pending = false;
    form.addEventListener("submit", event => {
        if (pending) { event.preventDefault(); return; }
        pending = true;
        form.querySelectorAll("button[type=submit]").forEach(button => { button.disabled = true; });
    });
});
document.querySelectorAll("[data-quiz-deadline]").forEach(timer => {
    const deadline = Date.parse(timer.dataset.quizDeadline);
    const update = () => {
        const remaining = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        timer.textContent = remaining > 0 ? `Time remaining: ${Math.floor(remaining / 60)}:${String(remaining % 60).padStart(2, "0")}. Server deadline is authoritative.` : "Deadline reached. Check history for server status; no answers are sent automatically.";
    };
    update();
    setInterval(update, 1000);
});
