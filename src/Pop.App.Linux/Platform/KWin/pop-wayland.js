const POP_PLUGIN_ID = "pop-wayland";
const SAMPLE_WINDOW_MS = 180;
const MIN_HORIZONTAL_VELOCITY = 1800;
const HORIZONTAL_DOMINANCE_RATIO = 1.75;

let sessions = new Map();

function now() {
    return Date.now();
}

function pointFromGeometry(geometry) {
    return {
        x: Number(geometry.x),
        y: Number(geometry.y),
        width: Number(geometry.width),
        height: Number(geometry.height),
        timestamp: now()
    };
}

function appendSample(session, geometry) {
    session.samples.push(pointFromGeometry(geometry));

    const cutoff = now() - SAMPLE_WINDOW_MS;
    while (session.samples.length > 2 && session.samples[0].timestamp < cutoff) {
        session.samples.shift();
    }
}

function isEligibleWindow(window) {
    return window &&
        window.normalWindow &&
        window.managed &&
        window.moveable &&
        window.resizeable &&
        !window.minimized &&
        !window.fullScreen &&
        !window.specialWindow;
}

function workAreaFor(window) {
    try {
        return workspace.clientArea(KWin.MaximizeArea, window);
    } catch (error) {
        return window.output ? window.output.geometry : workspace.virtualScreenGeometry;
    }
}

function snapWindow(window, target) {
    const area = workAreaFor(window);
    const width = Math.floor(Number(area.width) / 2);
    const height = Math.floor(Number(area.height));
    const x = target === "right"
        ? Math.floor(Number(area.x) + Number(area.width) - width)
        : Math.floor(Number(area.x));
    const y = Math.floor(Number(area.y));

    let geometry = Object.assign({}, window.frameGeometry);
    geometry.x = x;
    geometry.y = y;
    geometry.width = width;
    geometry.height = height;
    window.frameGeometry = geometry;
}

function finishSession(window) {
    const session = sessions.get(window);
    sessions.delete(window);

    if (!session || !isEligibleWindow(window)) {
        return;
    }

    appendSample(session, window.frameGeometry);

    if (session.samples.length < 2) {
        return;
    }

    const first = session.samples[0];
    const last = session.samples[session.samples.length - 1];
    const seconds = Math.max((last.timestamp - first.timestamp) / 1000, 0.001);
    const velocityX = (last.x - first.x) / seconds;
    const velocityY = (last.y - first.y) / seconds;
    const absX = Math.abs(velocityX);
    const absY = Math.abs(velocityY);

    if (absX < MIN_HORIZONTAL_VELOCITY || absX < absY * HORIZONTAL_DOMINANCE_RATIO) {
        return;
    }

    snapWindow(window, velocityX < 0 ? "left" : "right");
}

function connectWindow(window) {
    if (!window || window.__popConnected) {
        return;
    }

    window.__popConnected = true;

    window.interactiveMoveResizeStarted.connect(function () {
        if (window.resize || !isEligibleWindow(window)) {
            return;
        }

        sessions.set(window, {
            samples: [pointFromGeometry(window.frameGeometry)]
        });
    });

    window.interactiveMoveResizeStepped.connect(function (geometry) {
        const session = sessions.get(window);
        if (!session) {
            return;
        }

        appendSample(session, geometry);
    });

    window.interactiveMoveResizeFinished.connect(function () {
        finishSession(window);
    });
}

workspace.windowAdded.connect(connectWindow);
workspace.stackingOrder.forEach(connectWindow);

print("Pop Wayland KWin script loaded as " + POP_PLUGIN_ID);
