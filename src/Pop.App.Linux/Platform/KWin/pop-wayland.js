const POP_PLUGIN_ID = "pop-wayland";
const SAMPLE_WINDOW_MS = 180;
const POP_ENABLED = __POP_ENABLED__;
const GLIDE_DURATION_MS = __POP_GLIDE_DURATION_MS__;
const MIN_HORIZONTAL_VELOCITY = __POP_MIN_HORIZONTAL_VELOCITY__;
const HORIZONTAL_DOMINANCE_RATIO = __POP_HORIZONTAL_DOMINANCE_RATIO__;
const SNAP_RESTORE_TOLERANCE_PX = 96;
const MINIMUM_VISIBLE_WIDTH = 120;
const MINIMUM_VISIBLE_HEIGHT = 40;

let sessions = new Map();
let animationTimers = new Map();
let restoreStates = new Map();

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

function snapWindow(window, target, restoreBounds) {
    const area = workAreaFor(window);
    const width = Math.floor(Number(area.width) / 2);
    const height = Math.floor(Number(area.height));
    const x = target === "right"
        ? Math.floor(Number(area.x) + Number(area.width) - width)
        : Math.floor(Number(area.x));
    const y = Math.floor(Number(area.y));
    const snappedBounds = {
        x: x,
        y: y,
        width: width,
        height: height
    };

    restoreStates.set(window, {
        restoreBounds: cloneBounds(restoreBounds),
        snappedBounds: cloneBounds(snappedBounds)
    });
    animateWindow(window, snappedBounds);
}

function cloneBounds(bounds) {
    return {
        x: Number(bounds.x),
        y: Number(bounds.y),
        width: Number(bounds.width),
        height: Number(bounds.height)
    };
}

function maxX(bounds) {
    return Number(bounds.x) + Number(bounds.width);
}

function maxY(bounds) {
    return Number(bounds.y) + Number(bounds.height);
}

function areBoundsClose(first, second) {
    if (!first || !second) {
        return false;
    }

    return Math.abs(Number(first.x) - Number(second.x)) <= SNAP_RESTORE_TOLERANCE_PX &&
        Math.abs(Number(first.y) - Number(second.y)) <= SNAP_RESTORE_TOLERANCE_PX &&
        Math.abs(maxX(first) - maxX(second)) <= SNAP_RESTORE_TOLERANCE_PX &&
        Math.abs(maxY(first) - maxY(second)) <= SNAP_RESTORE_TOLERANCE_PX;
}

function clamp(value, minimum, maximum) {
    return minimum <= maximum
        ? Math.max(minimum, Math.min(maximum, value))
        : value;
}

function currentCursorPoint(window) {
    try {
        const cursor = workspace.cursorPos;
        if (cursor) {
            return {
                x: Number(cursor.x),
                y: Number(cursor.y)
            };
        }
    } catch (error) {
    }

    const geometry = window.frameGeometry;
    return {
        x: Number(geometry.x) + Math.round(Number(geometry.width) / 2),
        y: Number(geometry.y) + 18
    };
}

function createRestoreBounds(current, previous, pointer, workArea) {
    const width = Math.max(1, Math.round(Number(previous.width)));
    const height = Math.max(1, Math.round(Number(previous.height)));
    const relativeX = Number(current.width) <= 0
        ? 0.5
        : (Number(pointer.x) - Number(current.x)) / Number(current.width);
    const offsetX = Math.round(clamp(relativeX, 0, 1) * width);
    const offsetY = clamp(Math.round(Number(pointer.y) - Number(current.y)), 0, Math.max(0, height - 1));

    let x = Math.round(Number(pointer.x) - offsetX);
    let y = Math.round(Number(pointer.y) - offsetY);
    if (workArea) {
        const visibleWidth = Math.min(MINIMUM_VISIBLE_WIDTH, Number(workArea.width));
        const visibleHeight = Math.min(MINIMUM_VISIBLE_HEIGHT, Number(workArea.height));
        x = clamp(x, Number(workArea.x) - width + visibleWidth, Number(workArea.x) + Number(workArea.width) - visibleWidth);
        y = clamp(y, Number(workArea.y), Number(workArea.y) + Number(workArea.height) - visibleHeight);
    }

    return {
        x: x,
        y: y,
        width: width,
        height: height
    };
}

function restoreWindowIfNeeded(window) {
    const restoreState = restoreStates.get(window);
    const current = cloneBounds(window.frameGeometry);
    if (!restoreState) {
        return current;
    }

    if (!areBoundsClose(current, restoreState.snappedBounds)) {
        restoreStates.delete(window);
        return current;
    }

    restoreStates.delete(window);
    stopAnimation(window);
    const restored = createRestoreBounds(current, restoreState.restoreBounds, currentCursorPoint(window), workAreaFor(window));
    setFrameGeometry(window, restored);
    return restored;
}

function setFrameGeometry(window, target) {
    let geometry = Object.assign({}, window.frameGeometry);
    geometry.x = target.x;
    geometry.y = target.y;
    geometry.width = target.width;
    geometry.height = target.height;
    window.frameGeometry = geometry;
}

function easeOutCubic(progress) {
    return 1 - Math.pow(1 - progress, 3);
}

function interpolate(start, end, progress) {
    return Math.round(start + ((end - start) * progress));
}

function animateWindow(window, target) {
    stopAnimation(window);

    if (GLIDE_DURATION_MS <= 50) {
        setFrameGeometry(window, target);
        return;
    }

    if (typeof QTimer === "undefined") {
        setFrameGeometry(window, target);
        return;
    }

    const start = pointFromGeometry(window.frameGeometry);
    const startTime = now();

    function step() {
        if (!window || !isEligibleWindow(window)) {
            stopAnimation(window);
            return;
        }

        const progress = Math.min((now() - startTime) / GLIDE_DURATION_MS, 1);
        const eased = easeOutCubic(progress);
        setFrameGeometry(window, {
            x: interpolate(start.x, target.x, eased),
            y: interpolate(start.y, target.y, eased),
            width: interpolate(start.width, target.width, eased),
            height: interpolate(start.height, target.height, eased)
        });

        if (progress < 1) {
            if (!scheduleStep(window, step)) {
                setFrameGeometry(window, target);
            }
        } else {
            stopAnimation(window);
            setFrameGeometry(window, target);
        }
    }

    step();
}

function stopAnimation(window) {
    const timer = animationTimers.get(window);
    if (!timer) {
        return;
    }

    animationTimers.delete(window);
    try {
        timer.stop();
    } catch (error) {
    }
}

function scheduleStep(window, callback) {
    try {
        let timer = new QTimer();
        timer.singleShot = true;
        timer.timeout.connect(function () {
            if (animationTimers.get(window) === timer) {
                animationTimers.delete(window);
            }

            callback();
        });
        animationTimers.set(window, timer);
        timer.start(16);
        return true;
    } catch (error) {
        print("Pop could not schedule animation frame: " + error);
        return false;
    }
}

function finishSession(window) {
    const session = sessions.get(window);
    sessions.delete(window);

    if (!POP_ENABLED || !session || !isEligibleWindow(window)) {
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

    snapWindow(window, velocityX < 0 ? "left" : "right", session.initialBounds);
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

        const initialBounds = restoreWindowIfNeeded(window);
        sessions.set(window, {
            initialBounds: cloneBounds(initialBounds),
            samples: [pointFromGeometry(initialBounds)]
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
