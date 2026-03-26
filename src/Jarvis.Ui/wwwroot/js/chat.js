// Scroll the messages container to the bottom
export function scrollToBottom(elementId) {
    const el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
}

// Auto-grow textarea: expands up to max-height then scrolls internally
export function autoGrowTextarea(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;
    el.style.height = 'auto';
    const maxHeight = 144; // 6 rows × ~24px
    el.style.height = Math.min(el.scrollHeight, maxHeight) + 'px';
}

// Focus a DOM element by id
export function focusElement(elementId) {
    const el = document.getElementById(elementId);
    if (el) el.focus();
}

// localStorage helpers
export function getLocalStorage(key) {
    return localStorage.getItem(key);
}

export function setLocalStorage(key, value) {
    localStorage.setItem(key, value);
}

export function removeLocalStorage(key) {
    localStorage.removeItem(key);
}

// Mobile detection (< 768px)
export function isMobile() {
    return window.innerWidth < 768;
}

// Window resize → notify Blazor component
let resizeHandler = null;

export function onResize(dotnetRef) {
    resizeHandler = () => dotnetRef.invokeMethodAsync('OnWindowResize', window.innerWidth);
    window.addEventListener('resize', resizeHandler);
}

export function offResize() {
    if (resizeHandler) {
        window.removeEventListener('resize', resizeHandler);
        resizeHandler = null;
    }
}
