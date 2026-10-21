const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = fs.readFileSync(path.join(__dirname, "../LMS.Web/wwwroot/js/quiz.js"), "utf8");

test("duplicate clicks are blocked and the timer never submits or restarts an attempt", () => {
    let submitHandler;
    let tick;
    let now = Date.parse("2026-10-03T00:00:00Z");
    const button = { disabled: false };
    const form = {
        addEventListener(name, handler) { assert.equal(name, "submit"); submitHandler = handler; },
        querySelectorAll() { return [button]; },
        submit() { assert.fail("timer must never submit"); }
    };
    const timer = { dataset: { quizDeadline: "2026-10-03T00:01:05Z" }, textContent: "" };
    vm.runInNewContext(source, {
        document: { querySelectorAll(selector) { return selector.startsWith("form") ? [form] : [timer]; } },
        Date: { parse: Date.parse, now: () => now },
        setInterval(callback) { tick = callback; }
    });
    assert.match(timer.textContent, /1:05/);
    submitHandler({ preventDefault() { assert.fail("first click should submit"); } });
    assert.equal(button.disabled, true);
    let prevented = false;
    submitHandler({ preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    now = Date.parse("2026-10-03T00:02:00Z");
    tick();
    assert.match(timer.textContent, /Deadline reached/);
    assert.match(timer.textContent, /no answers are sent automatically/);
});
