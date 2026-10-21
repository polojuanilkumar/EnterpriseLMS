const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = fs.readFileSync(path.join(__dirname, "../LMS.Web/wwwroot/js/assignments.js"), "utf8");

test("final actions block duplicate clicks and history navigation never replays work", () => {
    let submitHandler, pageShowHandler;
    const button = { disabled: false };
    const form = {
        dataset: {},
        addEventListener(name, callback) { assert.equal(name, "submit"); submitHandler = callback; },
        querySelectorAll() { return [button]; },
        submit() { assert.fail("must never replay final work or grade"); }
    };
    vm.runInNewContext(source, {
        document: { querySelectorAll() { return [form]; } },
        window: { addEventListener(name, callback) { assert.equal(name, "pageshow"); pageShowHandler = callback; } },
        fetch() { assert.fail("must never send a background mutation"); },
        localStorage: { setItem() { assert.fail("must not persist student work in browser storage"); } }
    });
    submitHandler({ preventDefault() { assert.fail("first confirmed action may submit"); } });
    assert.equal(button.disabled, true);
    let blocked = false;
    submitHandler({ preventDefault() { blocked = true; } });
    assert.equal(blocked, true);
    pageShowHandler();
    assert.equal(button.disabled, false);
    assert.equal(form.dataset.sending, undefined);
});
