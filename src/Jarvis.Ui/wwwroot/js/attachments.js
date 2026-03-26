// Paste handler (Flameshot / Snipping Tool / clipboard images)
let pasteHandler = null;

export function registerPasteHandler(dotnetRef) {
    pasteHandler = async (e) => {
        const items = e.clipboardData?.items;
        if (!items) return;

        for (const item of items) {
            if (item.type.startsWith('image/')) {
                e.preventDefault();
                const file = item.getAsFile();
                if (!file) continue;

                const result = await readFileAsBase64(file);
                result.name = `screenshot_${Date.now()}.png`;
                await dotnetRef.invokeMethodAsync('OnFilePasted', result);
            }
        }
    };
    window.addEventListener('paste', pasteHandler);
}

export function unregisterPasteHandler() {
    if (pasteHandler) {
        window.removeEventListener('paste', pasteHandler);
        pasteHandler = null;
    }
}

// Drag-and-drop zone
const dropHandlers = {};

export function registerDropZone(elementId, dotnetRef) {
    const el = document.getElementById(elementId);
    if (!el) return;

    const onDragOver = (e) => {
        e.preventDefault();
        el.classList.add('drag-over');
    };

    const onDragLeave = (e) => {
        if (!el.contains(e.relatedTarget)) {
            el.classList.remove('drag-over');
        }
    };

    const onDrop = async (e) => {
        e.preventDefault();
        el.classList.remove('drag-over');
        const files = e.dataTransfer?.files;
        if (!files) return;

        for (const file of files) {
            const result = await readFileAsBase64(file);
            await dotnetRef.invokeMethodAsync('OnFileDropped', result);
        }
    };

    el.addEventListener('dragover', onDragOver);
    el.addEventListener('dragleave', onDragLeave);
    el.addEventListener('drop', onDrop);

    dropHandlers[elementId] = { onDragOver, onDragLeave, onDrop };
}

export function unregisterDropZone(elementId) {
    const el = document.getElementById(elementId);
    const handlers = dropHandlers[elementId];
    if (!el || !handlers) return;

    el.removeEventListener('dragover', handlers.onDragOver);
    el.removeEventListener('dragleave', handlers.onDragLeave);
    el.removeEventListener('drop', handlers.onDrop);
    delete dropHandlers[elementId];
}

// Read a File object and return { base64, mimeType, name, size }
export function readFileAsBase64(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
            const dataUrl = reader.result;
            const base64  = dataUrl.split(',')[1] ?? '';
            resolve({
                base64,
                mimeType: file.type || 'application/octet-stream',
                name:     file.name,
                size:     file.size,
            });
        };
        reader.onerror = reject;
        reader.readAsDataURL(file);
    });
}
