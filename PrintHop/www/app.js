document.addEventListener('DOMContentLoaded', () => {
    // State
    let selfInfo = null;
    let peers = [];
    let selectedPrinter = null; // { peer, printerName }
    let selectedFile = null;

    // DOM Elements - Primary
    const statusText = document.getElementById('local-status');
    const localPrinterList = document.getElementById('local-printer-list');
    const networkPrinterList = document.getElementById('network-printer-list');
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    const selectedFileContainer = document.getElementById('selected-file');
    const filenameDisplay = document.getElementById('filename-display');
    const removeFileBtn = document.getElementById('remove-file-btn');
    const printBtn = document.getElementById('print-btn');
    const toast = document.getElementById('toast');

    // Print Options Elements
    const copiesInput = document.getElementById('copies-input');
    const copiesDecBtn = document.getElementById('copies-dec');
    const copiesIncBtn = document.getElementById('copies-inc');
    const paperSizeSelect = document.getElementById('paper-size-select');
    const orientationSelect = document.getElementById('orientation-select');
    const colorModeSelect = document.getElementById('color-mode-select');
    const duplexSelect = document.getElementById('duplex-select');

    // Mobile Connect QR Modal Elements
    const showQrBtn = document.getElementById('show-qr-btn');
    const qrModal = document.getElementById('qr-modal');
    const closeQrBtn = document.getElementById('close-qr-btn');
    const qrcodeBox = document.getElementById('qrcode');
    const qrUrlInput = document.getElementById('qr-url-input');
    const copyUrlBtn = document.getElementById('copy-url-btn');

    // Activity & Devices State & Elements
    let activityLogs = [];
    let devices = [];
    let activeLogFilter = 'all';

    const navTabs = document.querySelectorAll('.nav-item');
    const viewPanels = document.querySelectorAll('.view-panel');
    const subnavBtns = document.querySelectorAll('.segment-btn');
    const subviewPanels = document.querySelectorAll('.subview-content');
    const activityBadge = document.getElementById('activity-badge');

    const statTotalPrints = document.getElementById('stat-total-prints');
    const statTotalDevices = document.getElementById('stat-total-devices');
    const statBlockedDevices = document.getElementById('stat-blocked-devices');

    const refreshActivityBtn = document.getElementById('refresh-activity-btn');
    const clearLogsBtn = document.getElementById('clear-logs-btn');
    const filterTabs = document.querySelectorAll('.filter-tabs .filter-tab');
    const activityLogList = document.getElementById('activity-log-list');
    const devicesList = document.getElementById('devices-list');
    const manualBlockInput = document.getElementById('manual-block-input');
    const manualBlockBtn = document.getElementById('manual-block-btn');

    // Stepper Listeners
    if (copiesDecBtn && copiesIncBtn && copiesInput) {
        copiesDecBtn.addEventListener('click', () => {
            let val = parseInt(copiesInput.value, 10) || 1;
            if (val > 1) copiesInput.value = val - 1;
        });

        copiesIncBtn.addEventListener('click', () => {
            let val = parseInt(copiesInput.value, 10) || 1;
            if (val < 99) copiesInput.value = val + 1;
        });

        copiesInput.addEventListener('change', () => {
            let val = parseInt(copiesInput.value, 10);
            if (isNaN(val) || val < 1) copiesInput.value = 1;
            else if (val > 99) copiesInput.value = 99;
        });
    }

    // QR Modal Listeners
    if (showQrBtn && qrModal) {
        showQrBtn.addEventListener('click', () => {
            setupMobileConnect();
            qrModal.classList.remove('hidden');
        });

        if (closeQrBtn) {
            closeQrBtn.addEventListener('click', () => {
                qrModal.classList.add('hidden');
            });
        }

        qrModal.addEventListener('click', (e) => {
            if (e.target === qrModal) {
                qrModal.classList.add('hidden');
            }
        });

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && !qrModal.classList.contains('hidden')) {
                qrModal.classList.add('hidden');
            }
        });
    }

    if (copyUrlBtn && qrUrlInput) {
        copyUrlBtn.addEventListener('click', async () => {
            try {
                await navigator.clipboard.writeText(qrUrlInput.value);
                copyUrlBtn.textContent = 'Copied';
                showToast('URL copied to clipboard', 'success');
                setTimeout(() => { copyUrlBtn.textContent = 'Copy'; }, 2000);
            } catch {
                qrUrlInput.select();
                document.execCommand('copy');
                copyUrlBtn.textContent = 'Copied';
                showToast('URL copied to clipboard', 'success');
                setTimeout(() => { copyUrlBtn.textContent = 'Copy'; }, 2000);
            }
        });
    }

    function setupMobileConnect() {
        if (!selfInfo) return;
        const mobileIp = (selfInfo.ip && selfInfo.ip !== '127.0.0.1') ? selfInfo.ip : window.location.hostname;
        const mobilePort = selfInfo.httpPort || window.location.port || 4222;
        const mobileUrl = `http://${mobileIp}:${mobilePort}`;

        if (qrUrlInput) {
            qrUrlInput.value = mobileUrl;
        }

        if (qrcodeBox && typeof QRCode !== 'undefined') {
            qrcodeBox.innerHTML = '';
            new QRCode(qrcodeBox, {
                text: mobileUrl,
                width: 160,
                height: 160,
                colorDark: "#0f172a",
                colorLight: "#ffffff",
                correctLevel: QRCode.CorrectLevel.M
            });
        }
    }

    // Navigation Tabs Switching
    navTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const targetViewId = tab.getAttribute('data-view');
            navTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');

            viewPanels.forEach(p => {
                if (p.id === targetViewId) {
                    p.classList.remove('hidden');
                } else {
                    p.classList.add('hidden');
                }
            });
            if (targetViewId === 'activity-view') {
                fetchActivityLogs();
                fetchDevices();
                if (typeof fetchPrintQueue === 'function') fetchPrintQueue();
            }
        });
    });

    // Sub Navigation (Logs vs Devices)
    subnavBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const targetSubId = btn.getAttribute('data-subview');
            subnavBtns.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            subviewPanels.forEach(p => {
                if (p.id === targetSubId) {
                    p.classList.remove('hidden');
                } else {
                    p.classList.add('hidden');
                }
            });
        });
    });

    // Filter Tabs (All / Success / Blocked / Failed)
    filterTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            filterTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            activeLogFilter = tab.getAttribute('data-filter');
            renderActivityLogs();
        });
    });

    // Manual Block Action
    if (manualBlockBtn && manualBlockInput) {
        manualBlockBtn.addEventListener('click', () => {
            const val = manualBlockInput.value.trim();
            if (!val || val.length < 2 || val.length > 128) {
                showToast('Please enter a valid device ID or hostname to block', 'error');
                return;
            }
            const sanitized = val.replace(/[<>"'/\\&]/g, '');
            if (!sanitized) {
                showToast('Invalid device identifier entered', 'error');
                return;
            }
            blockDevice(sanitized, sanitized);
            manualBlockInput.value = '';
        });

        manualBlockInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                manualBlockBtn.click();
            }
        });
    }

    // Refresh & Clear Action Buttons
    if (refreshActivityBtn) {
        refreshActivityBtn.addEventListener('click', async () => {
            await Promise.all([fetchActivityLogs(), fetchDevices()]);
            showToast('Activity and device records refreshed', 'success');
        });
    }

    if (clearLogsBtn) {
        clearLogsBtn.addEventListener('click', async () => {
            if (!confirm('Clear all print activity audit history?')) return;
            try {
                const res = await fetch('/api/activity-logs/clear', { method: 'POST' });
                if (res.ok) {
                    activityLogs = [];
                    renderActivityLogs();
                    updateStats();
                    showToast('Activity history cleared', 'success');
                }
            } catch {
                showToast('Failed to clear activity history', 'error');
            }
        });
    }

    function isMobileClient() {
        return /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent);
    }

    // Initialize App
    async function init() {
        try {
            await fetchSelf();
            await fetchPeers();
            await fetchActivityLogs();
            await fetchDevices();

            if (isMobileClient()) {
                const dropPrimary = document.getElementById('drop-text-primary');
                if (dropPrimary) dropPrimary.textContent = 'Tap to select a document';
            }

            // Poll for peers every 5 seconds
            setInterval(fetchPeers, 5000);
            // Poll for activity logs and devices every 3 seconds
            setInterval(() => {
                fetchActivityLogs();
                fetchDevices();
            }, 3000);
        } catch {
            statusText.textContent = 'Offline';
            const dot = document.querySelector('.status-dot');
            if (dot) dot.style.backgroundColor = 'var(--danger)';
        }
    }

    async function fetchSelf() {
        const res = await fetch('/api/self');
        if (!res.ok) throw new Error('Network error');
        selfInfo = await res.json();
        statusText.textContent = `Host: ${selfInfo.hostname}`;
        setupMobileConnect();
    }

    let lastPeersSignature = '';

    async function fetchPeers() {
        try {
            const res = await fetch('/api/peers');
            if (!res.ok) return;
            const newPeers = await res.json();
            peers = newPeers || [];

            const allPeers = selfInfo ? [selfInfo, ...peers] : peers;
            const signature = JSON.stringify(allPeers.map(p => ({
                id: p.id || p.Id,
                hostname: p.hostname || p.Hostname,
                printers: p.printers || p.Printers || []
            })));

            if (signature !== lastPeersSignature) {
                lastPeersSignature = signature;
                renderPrinters(allPeers);
            }
        } catch (error) {
            console.error('Failed to fetch peers:', error);
        }
    }

    // =========================================================================
    // Printer Separation: Local System Printers vs Network Printers
    // =========================================================================
    function renderPrinters(allPeers) {
        if (!localPrinterList || !networkPrinterList) return;

        const selfId = selfInfo ? (selfInfo.id || selfInfo.Id) : null;
        const hostName = selfInfo ? (selfInfo.hostname || 'Host PC') : 'Host PC';
        const isMobile = isMobileClient();

        // Update category titles based on mobile vs desktop
        const primaryTitle = document.getElementById('primary-category-title');
        const primarySubtitle = document.getElementById('primary-category-subtitle');
        if (primaryTitle && primarySubtitle) {
            if (isMobile) {
                primaryTitle.textContent = `Host Printers (${hostName})`;
                primarySubtitle.textContent = 'Installed on connected computer';
            } else {
                primaryTitle.textContent = 'Local System Printers';
                primarySubtitle.textContent = 'Installed on this computer';
            }
        }

        let localCardsHtml = [];
        let networkCardsHtml = [];

        // Build list of printers
        allPeers.forEach(peer => {
            if (!peer) return;
            const peerPrinters = peer.printers || peer.Printers;
            if (!peerPrinters || peerPrinters.length === 0) return;

            const peerId = peer.id || peer.Id;
            const peerHostname = peer.hostname || peer.Hostname || 'Unknown Host';
            const isSelf = (peerId === selfId) || (selfInfo && (peer.ip === selfInfo.ip || peer.Ip === selfInfo.ip));

            peerPrinters.forEach(printer => {
                const isSelected = selectedPrinter &&
                                   (selectedPrinter.peer.id || selectedPrinter.peer.Id) === peerId &&
                                   selectedPrinter.printerName === printer;

                const cardData = {
                    peer,
                    peerId,
                    peerHostname,
                    printer,
                    isSelf,
                    isSelected
                };

                if (isSelf) {
                    localCardsHtml.push(cardData);
                } else {
                    networkCardsHtml.push(cardData);
                }
            });
        });

        // Render Local Printers
        if (localCardsHtml.length === 0) {
            localPrinterList.innerHTML = `<div class="empty-notice">No printers detected.</div>`;
        } else {
            localPrinterList.innerHTML = '';
            localCardsHtml.forEach(data => {
                const card = createPrinterCardElement(data, true);
                localPrinterList.appendChild(card);
            });
        }

        // Render Network Printers
        if (networkCardsHtml.length === 0) {
            networkPrinterList.innerHTML = `<div class="empty-notice">No remote network printers found on your LAN. Connect other computers running PrintHop to share printers.</div>`;
        } else {
            networkPrinterList.innerHTML = '';
            networkCardsHtml.forEach(data => {
                const card = createPrinterCardElement(data, false);
                networkPrinterList.appendChild(card);
            });
        }

        // Auto-select first printer if none is selected
        if (!selectedPrinter) {
            const firstCard = document.querySelector('.printer-card');
            if (firstCard) firstCard.click();
        }
    }

    function createPrinterCardElement(data, isLocal) {
        const card = document.createElement('div');
        card.className = `printer-card ${data.isSelected ? 'selected' : ''}`;
        card.dataset.peerId = data.peerId;
        card.dataset.printer = data.printer;

        const isMobile = isMobileClient();
        const hostName = selfInfo ? (selfInfo.hostname || 'Host PC') : 'Host PC';

        let hostTag = '';
        let badgeLabel = '';
        let statusDetail = 'Ready';

        if (isLocal) {
            hostTag = isMobile ? hostName : 'This PC';
            badgeLabel = isMobile ? 'Host PC' : 'Local';
        } else {
            hostTag = data.peerHostname;
            badgeLabel = 'Network';
        }

        if (/pdf|onenote|xps|fax/i.test(data.printer)) {
            statusDetail = 'Virtual';
        }

        card.innerHTML = `
            <div class="printer-card-meta">
                <span class="printer-card-host">${escapeHtml(hostTag)}</span>
                <span class="printer-card-badge">${badgeLabel}</span>
            </div>
            <div class="printer-card-name">${escapeHtml(data.printer)}</div>
            <div class="printer-card-status">
                <span class="status-dot"></span>
                <span>${escapeHtml(statusDetail)}</span>
            </div>
        `;

        card.addEventListener('click', () => {
            document.querySelectorAll('.printer-card').forEach(c => c.classList.remove('selected'));
            card.classList.add('selected');
            selectedPrinter = { peer: data.peer, printerName: data.printer };
            updatePrintButton();
            fetchPrinterCapabilities(data.peer, data.printer);
        });

        return card;
    }

    // Dynamic Capabilities Negotiation (Zero Emojis)
    async function fetchPrinterCapabilities(peer, printerName) {
        const subtitle = document.getElementById('options-subtitle');
        const badgesContainer = document.getElementById('caps-badges');
        const paperHint = document.getElementById('paper-hint');
        const colorHint = document.getElementById('color-hint');
        const duplexHint = document.getElementById('duplex-hint');
        const copiesHint = document.getElementById('copies-hint');

        const peerHostname = peer.hostname || peer.Hostname || 'Printer';
        if (subtitle) subtitle.textContent = `Loading capabilities for ${printerName}...`;
        if (badgesContainer) badgesContainer.innerHTML = `<span class="caps-tag">Loading capabilities...</span>`;

        try {
            const peerId = peer.id || peer.Id;
            const selfId = selfInfo ? (selfInfo.id || selfInfo.Id) : null;
            const isLocal = !peerId || (peerId === selfId) || (selfInfo && (peer.ip === selfInfo.ip || peer.Ip === selfInfo.ip));

            const url = isLocal
                ? `/api/printer-capabilities?name=${encodeURIComponent(printerName)}`
                : `http://${peer.ip || peer.Ip}:${peer.httpPort || peer.HttpPort || 4222}/api/printer-capabilities?name=${encodeURIComponent(printerName)}`;

            const res = await fetch(url);
            if (!res.ok) throw new Error('Failed to query driver capabilities');
            const caps = await res.json();

            const supportsColor = (caps.supportsColor !== undefined) ? caps.supportsColor : (caps.SupportsColor !== undefined ? caps.SupportsColor : true);
            const canDuplex = (caps.canDuplex !== undefined) ? caps.canDuplex : (caps.CanDuplex !== undefined ? caps.CanDuplex : false);
            const paperSizes = caps.paperSizes || caps.PaperSizes || [];
            const defaultPaperSize = caps.defaultPaperSize || caps.DefaultPaperSize || 'Auto';
            const maxCopies = (caps.maxCopies !== undefined) ? caps.maxCopies : (caps.MaxCopies !== undefined ? caps.MaxCopies : 999);

            if (subtitle) subtitle.textContent = `Configured for ${printerName} (${peerHostname})`;

            // Hardware Badges
            if (badgesContainer) {
                let badgesHtml = '';
                badgesHtml += supportsColor
                    ? `<span class="caps-tag">Color</span>`
                    : `<span class="caps-tag">Monochrome</span>`;

                if (canDuplex) {
                    badgesHtml += `<span class="caps-tag">Two-Sided (Duplex)</span>`;
                }

                if (paperSizes.length > 0) {
                    badgesHtml += `<span class="caps-tag">${paperSizes.length} Paper Sizes</span>`;
                }
                badgesContainer.innerHTML = badgesHtml;
            }

            // Paper Size Dropdown
            if (paperSizeSelect) {
                const currentVal = paperSizeSelect.value;
                paperSizeSelect.innerHTML = '';

                const defOpt = document.createElement('option');
                defOpt.value = 'Default';
                defOpt.textContent = `Default (${defaultPaperSize})`;
                paperSizeSelect.appendChild(defOpt);

                if (paperSizes.length > 0) {
                    paperSizes.forEach(sz => {
                        const opt = document.createElement('option');
                        opt.value = sz;
                        opt.textContent = sz;
                        paperSizeSelect.appendChild(opt);
                    });
                } else {
                    ['A4', 'A5', 'Letter', 'Legal'].forEach(sz => {
                        const opt = document.createElement('option');
                        opt.value = sz;
                        opt.textContent = sz;
                        paperSizeSelect.appendChild(opt);
                    });
                }

                const matchOption = Array.from(paperSizeSelect.options).find(o => o.value === currentVal);
                if (matchOption) {
                    paperSizeSelect.value = currentVal;
                } else {
                    paperSizeSelect.value = 'Default';
                }

                if (paperHint) {
                    paperHint.textContent = `${paperSizes.length} paper formats supported by driver`;
                }
            }

            // Color Mode Lock
            if (colorModeSelect) {
                const colorOption = colorModeSelect.querySelector('option[value="Color"]');
                const grayOption = colorModeSelect.querySelector('option[value="Grayscale"]');

                if (!supportsColor) {
                    colorModeSelect.value = 'Grayscale';
                    if (colorOption) colorOption.disabled = true;
                    if (colorHint) colorHint.textContent = 'Monochrome printer';
                } else {
                    if (colorOption) colorOption.disabled = false;
                    if (colorHint) colorHint.textContent = 'Full color support';
                }
            }

            // Duplex Mode Lock
            if (duplexSelect) {
                const longEdge = duplexSelect.querySelector('option[value="TwoSidedLongEdge"]');
                const shortEdge = duplexSelect.querySelector('option[value="TwoSidedShortEdge"]');

                if (!canDuplex) {
                    duplexSelect.value = 'Simplex';
                    if (longEdge) longEdge.disabled = true;
                    if (shortEdge) shortEdge.disabled = true;
                    if (duplexHint) duplexHint.textContent = 'Single-sided only';
                } else {
                    if (longEdge) longEdge.disabled = false;
                    if (shortEdge) shortEdge.disabled = false;
                    if (duplexHint) duplexHint.textContent = 'Hardware two-sided duplex';
                }
            }

            // Max Copies
            if (copiesInput) {
                copiesInput.max = maxCopies > 0 ? maxCopies : 99;
                if (copiesHint) {
                    copiesHint.textContent = `Driver supports up to ${copiesInput.max} copies`;
                }
            }
        } catch {
            if (subtitle) subtitle.textContent = `Configured with standard defaults for ${printerName}`;
            if (badgesContainer) badgesContainer.innerHTML = `<span class="caps-tag">Standard Profile</span>`;
        }
    }

    // Drag & Drop File Upload
    dropZone.addEventListener('click', () => fileInput.click());

    ['dragenter', 'dragover'].forEach(eventName => {
        dropZone.addEventListener(eventName, (e) => {
            e.preventDefault();
            dropZone.classList.add('dragover');
        });
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropZone.addEventListener(eventName, (e) => {
            e.preventDefault();
            dropZone.classList.remove('dragover');
        });
    });

    dropZone.addEventListener('drop', (e) => {
        if (e.dataTransfer.files.length > 0) {
            handleFileSelect(e.dataTransfer.files[0]);
        }
    });

    fileInput.addEventListener('change', () => {
        if (fileInput.files.length > 0) {
            handleFileSelect(fileInput.files[0]);
        }
    });

    removeFileBtn.addEventListener('click', () => {
        selectedFile = null;
        fileInput.value = '';
        selectedFileContainer.classList.add('hidden');
        dropZone.style.display = 'flex';
        updatePrintButton();
    });

    function handleFileSelect(file) {
        if (!file) return;
        const maxBytes = 500 * 1024 * 1024;
        if (file.size > maxBytes) {
            showToast('File exceeds the 500 MB maximum size limit.', 'error');
            removeFileBtn.click();
            return;
        }
        selectedFile = file;
        filenameDisplay.textContent = file.name;
        selectedFileContainer.classList.remove('hidden');
        dropZone.style.display = 'none';
        updatePrintButton();
    }

    function updatePrintButton() {
        printBtn.disabled = !(selectedPrinter && selectedFile);
    }

    // Print Dispatch Action
    printBtn.addEventListener('click', async () => {
        if (!selectedPrinter || !selectedFile || !selfInfo) return;

        printBtn.disabled = true;
        printBtn.textContent = 'Dispatching...';

        const headers = {
            'X-PrintHop-SenderId': selfInfo.id,
            'X-PrintHop-SenderHostname': selfInfo.hostname,
            'X-PrintHop-PrinterName': selectedPrinter.printerName,
            'X-PrintHop-OriginalFilename': encodeURIComponent(selectedFile.name)
        };

        if (copiesInput) headers['X-PrintHop-Copies'] = copiesInput.value || '1';
        if (paperSizeSelect) headers['X-PrintHop-PaperSize'] = paperSizeSelect.value || 'Default';
        if (orientationSelect) headers['X-PrintHop-Orientation'] = orientationSelect.value || 'Portrait';
        if (colorModeSelect) headers['X-PrintHop-ColorMode'] = colorModeSelect.value || 'Color';
        if (duplexSelect) headers['X-PrintHop-Duplex'] = duplexSelect.value || 'Simplex';

        try {
            const targetPeer = selectedPrinter.peer;
            const targetPeerId = targetPeer.id || targetPeer.Id;
            const selfId = selfInfo ? (selfInfo.id || selfInfo.Id) : null;
            const isLocal = !targetPeerId || (targetPeerId === selfId) || (selfInfo && ((targetPeer.ip || targetPeer.Ip) === selfInfo.ip));

            const targetUrl = isLocal
                ? `/api/receive-print`
                : `http://${targetPeer.ip || targetPeer.Ip}:${targetPeer.httpPort || targetPeer.HttpPort || 4222}/api/receive-print`;

            const res = await fetch(targetUrl, {
                method: 'POST',
                headers: headers,
                body: selectedFile
            });

            if (res.ok) {
                try {
                    const data = await res.json();
                    if (data && data.success && data.jobId) {
                        // Register this job locally so we accept webhooks for it
                        await fetch('/api/jobs/track', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ jobId: data.jobId })
                        });
                    }
                } catch (e) {
                    // Fallback if not json
                }
                showToast('Print job dispatched successfully', 'success');
                removeFileBtn.click();
                fetchActivityLogs();
                fetchDevices();
            } else {
                const text = await res.text();
                showToast(`Print failed: ${text}`, 'error');
            }
        } catch {
            showToast('Network error while dispatching print job', 'error');
        } finally {
            printBtn.textContent = 'Print Document';
            updatePrintButton();
        }
    });

    // =========================================================================
    // Activity & Device Management Logic (Zero Emojis)
    // =========================================================================
    async function fetchActivityLogs() {
        try {
            const res = await fetch('/api/activity-logs?limit=50');
            if (!res.ok) return;
            activityLogs = await res.json();
            renderActivityLogs();
            updateStats();
        } catch (e) {
            console.error('Failed to fetch activity logs:', e);
        }
    }

    async function fetchDevices() {
        try {
            const res = await fetch('/api/devices');
            if (!res.ok) return;
            devices = await res.json();
            renderDevices();
            updateStats();
        } catch (e) {
            console.error('Failed to fetch devices:', e);
        }
    }

    async function fetchPrintQueue() {
        try {
            const res = await fetch('/api/jobs');
            if (!res.ok) return;
            const queue = await res.json();
            renderPrintQueue(queue);
        } catch (e) {
            console.error('Failed to fetch print queue:', e);
        }
    }

    function renderPrintQueue(queue) {
        const queueList = document.getElementById('print-queue-list');
        if (!queueList) return;

        queueList.innerHTML = '';
        if (!queue || queue.length === 0) {
            queueList.innerHTML = '<div class="empty-notice">No print jobs currently in the queue.</div>';
            return;
        }

        queue.forEach(job => {
            const el = document.createElement('div');
            el.className = 'activity-item';

            const statusClass = job.Status === 'Printing' ? 'status-success' : 'status-unknown';
            const blockBtnHtml = job.Status === 'Queued' ? `<button class="btn-danger-ghost block-job-btn" style="margin-left:auto; padding:4px 8px; font-size:12px;" data-id="${job.Id}">Block</button>` : `<span style="margin-left:auto; font-size:12px; color:var(--text-secondary);">${job.Status}</span>`;
            
            // Format time correctly from C# datetime serialization
            let timeStr = 'Unknown time';
            if (job.Timestamp) {
                const match = new RegExp('\\\\/Date\\((\\d+)\\)\\\\/').exec(job.Timestamp) || new RegExp('\\/Date\\((\\d+)\\)\\/').exec(job.Timestamp);
                if (match) {
                    timeStr = new Date(parseInt(match[1])).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'});
                } else {
                    timeStr = new Date(job.Timestamp).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'});
                }
            }

            el.innerHTML = `
                <div class="activity-icon ${statusClass}">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="6 9 6 2 18 2 18 9"></polyline>
                        <path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"></path>
                        <rect x="6" y="14" width="12" height="8"></rect>
                    </svg>
                </div>
                <div class="activity-content" style="flex:1;">
                    <div class="activity-header">
                        <span class="activity-title">${escapeHtml(job.DocumentName)} &rarr; ${escapeHtml(job.PrinterName)}</span>
                        <span class="activity-time">${timeStr}</span>
                    </div>
                    <div class="activity-body">
                        <p class="activity-desc">From: <strong>${escapeHtml(job.SenderHostname)}</strong> | Status: <strong>${escapeHtml(job.Status)}</strong></p>
                    </div>
                </div>
                <div class="activity-actions">
                    ${blockBtnHtml}
                </div>
            `;
            queueList.appendChild(el);
        });

        document.querySelectorAll('.block-job-btn').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                const id = e.target.getAttribute('data-id');
                if (confirm('Are you sure you want to block and cancel this queued job?')) {
                    try {
                        const res = await fetch('/api/jobs/block?id=' + encodeURIComponent(id), { method: 'POST' });
                        if (res.ok) {
                            showToast('Job blocked successfully', 'success');
                            fetchPrintQueue();
                        } else {
                            const data = await res.json();
                            showToast(data.message || 'Failed to block job', 'error');
                        }
                    } catch {
                        showToast('Network error while blocking job', 'error');
                    }
                }
            });
        });
    }

    // Periodically refresh the queue if the subview is open
    setInterval(() => {
        const activityView = document.getElementById('activity-view');
        const queueView = document.getElementById('subview-queue');
        if (activityView && !activityView.classList.contains('hidden') && queueView && !queueView.classList.contains('hidden')) {
            fetchPrintQueue();
        }
    }, 2000);

    function updateStats() {
        if (statTotalPrints) {
            let total = 0;
            if (devices && devices.length > 0) {
                total = devices.reduce((sum, d) => sum + (d.TotalPrints || 0), 0);
            }
            if (total === 0 && activityLogs) {
                total = activityLogs.filter(l => l.Status === 'Success').length;
            }
            statTotalPrints.textContent = total;
        }

        if (statTotalDevices) {
            statTotalDevices.textContent = devices ? devices.length : 0;
        }

        if (statBlockedDevices) {
            const blockedCount = devices ? devices.filter(d => d.IsBlocked).length : 0;
            statBlockedDevices.textContent = blockedCount;
        }

        if (activityBadge) {
            if (activityLogs && activityLogs.length > 0) {
                activityBadge.textContent = activityLogs.length;
                activityBadge.classList.remove('hidden');
            } else {
                activityBadge.classList.add('hidden');
            }
        }
    }

    function renderActivityLogs() {
        if (!activityLogList) return;

        let filtered = activityLogs;
        if (activeLogFilter === 'Success') {
            filtered = activityLogs.filter(l => l.Status === 'Success');
        } else if (activeLogFilter === 'Blocked') {
            filtered = activityLogs.filter(l => l.Status === 'Blocked');
        } else if (activeLogFilter === 'Failed') {
            filtered = activityLogs.filter(l => l.Status === 'Failed' || l.Status === 'Rejected');
        }

        if (!filtered || filtered.length === 0) {
            activityLogList.innerHTML = `<div class="empty-notice">No print audit logs recorded.</div>`;
            return;
        }

        activityLogList.innerHTML = filtered.map(log => {
            const logDate = parseNetDate(log.Timestamp);
            const timeStr = formatRelativeTime(logDate);
            const fullDateStr = logDate.toLocaleString();
            const statusClass = (log.Status || 'Failed').toLowerCase();
            const isBlocked = isDeviceCurrentlyBlocked(log.SenderId);

            let statusLabel = 'Success';
            if (log.Status === 'Blocked') statusLabel = 'Blocked';
            else if (log.Status === 'Rejected') statusLabel = 'Rejected';
            else if (log.Status === 'Failed') statusLabel = 'Failed';

            const docName = escapeHtml(log.DocumentName || 'Document');
            const hostname = escapeHtml(log.SenderHostname || 'Unknown Device');
            const printer = escapeHtml(log.PrinterName || 'Printer');
            const options = escapeHtml(log.OptionsSummary || '');
            const message = escapeHtml(log.Message || '');
            const senderId = escapeHtml(log.SenderId || '');

            return `
                <div class="activity-row-card">
                    <div class="activity-top-line">
                        <div class="activity-device-info">
                            <span>${log.IsLocal ? 'Local Workstation' : hostname}</span>
                            ${senderId ? `<span class="device-id-tag" title="${senderId}">ID: ${senderId.substring(0, 8)}</span>` : ''}
                        </div>
                        <span class="activity-timestamp" title="${fullDateStr}">${timeStr}</span>
                    </div>

                    <div class="activity-middle-line">
                        <div class="doc-info">
                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                                <polyline points="14 2 14 8 20 8"></polyline>
                            </svg>
                            <span>${docName}</span>
                            <span class="printer-target-tag">Target: ${printer}</span>
                        </div>

                        ${options ? `<div class="options-chip-wrap"><span class="opt-chip">${options}</span></div>` : ''}
                    </div>

                    <div class="activity-bottom-line">
                        <div class="status-and-msg">
                            <span class="status-pill ${statusClass}">${statusLabel}</span>
                            ${message ? `<span class="activity-note">${message}</span>` : ''}
                        </div>

                        ${(!log.IsLocal && senderId) ? `
                            <div class="activity-actions">
                                ${isBlocked 
                                    ? `<button class="btn-secondary-sm" data-action="unblock" data-device-id="${senderId}">Unblock</button>`
                                    : `<button class="btn-danger-ghost" data-action="block" data-device-id="${senderId}" data-hostname="${hostname}">Block Device</button>`
                                }
                            </div>
                        ` : ''}
                    </div>
                </div>
            `;
        }).join('');
    }

    function isDeviceCurrentlyBlocked(deviceId) {
        if (!deviceId || !devices) return false;
        const dev = devices.find(d => d.Id && d.Id.toLowerCase() === deviceId.toLowerCase());
        return dev ? dev.IsBlocked : false;
    }

    function renderDevices() {
        if (!devicesList) return;

        if (!devices || devices.length === 0) {
            devicesList.innerHTML = `<div class="empty-notice">No recognized network devices recorded.</div>`;
            return;
        }

        devicesList.innerHTML = devices.map(dev => {
            const hostname = escapeHtml(dev.Hostname || 'Unknown Device');
            const devId = escapeHtml(dev.Id || '');
            const totalPrints = dev.TotalPrints || 0;
            const lastSeen = parseNetDate(dev.LastSeen);
            const timeStr = formatRelativeTime(lastSeen);
            const isLocal = selfInfo && devId === selfInfo.id;

            let statusPill = '';
            if (dev.IsBlocked) {
                statusPill = '<span class="status-pill blocked">Blocked</span>';
            } else if (dev.IsApproved) {
                statusPill = '<span class="status-pill success">Authorized</span>';
            } else {
                statusPill = '<span class="status-pill rejected">Pending</span>';
            }

            return `
                <div class="device-card-row ${dev.IsBlocked ? 'blocked' : ''}">
                    <div class="device-left-meta">
                        <div class="device-avatar-box">
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect>
                                <line x1="8" y1="21" x2="16" y2="21"></line>
                                <line x1="12" y1="17" x2="12" y2="21"></line>
                            </svg>
                        </div>
                        <div class="device-identity">
                            <div class="device-title-line">
                                <span>${hostname}</span>
                                ${statusPill}
                            </div>
                            <div class="device-detail-line">
                                <span class="device-id-tag" title="${devId}">ID: ${devId.substring(0, 10)}</span>
                                <span>&bull;</span>
                                <span>${totalPrints} print job${totalPrints === 1 ? '' : 's'}</span>
                                <span>&bull;</span>
                                <span>Active ${timeStr}</span>
                            </div>
                        </div>
                    </div>

                    <div class="device-buttons">
                        ${isLocal ? '<span class="opt-chip">Host PC</span>' : `
                            ${dev.IsBlocked
                                ? `<button class="btn-success-action" data-action="unblock" data-device-id="${devId}">Unblock</button>`
                                : `<button class="btn-danger-action" data-action="block" data-device-id="${devId}" data-hostname="${hostname}">Block</button>`
                            }
                            ${(!dev.IsBlocked && !dev.IsApproved)
                                ? `<button class="btn-neutral-action" data-action="approve" data-device-id="${devId}" data-hostname="${hostname}">Authorize</button>`
                                : ''
                            }
                            ${(!dev.IsBlocked && dev.IsApproved)
                                ? `<button class="btn-neutral-action" data-action="revoke" data-device-id="${devId}">Revoke</button>`
                                : ''
                            }
                        `}
                    </div>
                </div>
            `;
        }).join('');
    }

    // Secure Event Delegation Listeners (Prevents inline XSS)
    if (activityLogList) {
        activityLogList.addEventListener('click', async (e) => {
            const btn = e.target.closest('button[data-action]');
            if (!btn) return;
            const action = btn.getAttribute('data-action');
            const id = btn.getAttribute('data-device-id');
            const hostname = btn.getAttribute('data-hostname') || '';
            if (action === 'block') {
                if (!confirm(`Block device '${hostname || id}' from printing?`)) return;
                await blockDevice(id, hostname);
            } else if (action === 'unblock') {
                await unblockDevice(id);
            }
        });
    }

    if (devicesList) {
        devicesList.addEventListener('click', async (e) => {
            const btn = e.target.closest('button[data-action]');
            if (!btn) return;
            const action = btn.getAttribute('data-action');
            const id = btn.getAttribute('data-device-id');
            const hostname = btn.getAttribute('data-hostname') || '';
            if (action === 'block') {
                if (!confirm(`Block device '${hostname || id}' from printing?`)) return;
                await blockDevice(id, hostname);
            } else if (action === 'unblock') {
                await unblockDevice(id);
            } else if (action === 'approve') {
                await approveDevice(id, hostname);
            } else if (action === 'revoke') {
                await revokeDevice(id);
            }
        });
    }

    async function blockDevice(id, hostname) {
        try {
            const url = `/api/devices/block?id=${encodeURIComponent(id)}&hostname=${encodeURIComponent(hostname || '')}`;
            const res = await fetch(url, { method: 'POST' });
            if (res.ok) {
                showToast(`Device '${hostname || id}' blocked`, 'success');
                await Promise.all([fetchActivityLogs(), fetchDevices()]);
            } else {
                const text = await res.text();
                showToast(`Block failed: ${text}`, 'error');
            }
        } catch {
            showToast('Network error while blocking device', 'error');
        }
    }

    async function unblockDevice(id) {
        try {
            const url = `/api/devices/unblock?id=${encodeURIComponent(id)}`;
            const res = await fetch(url, { method: 'POST' });
            if (res.ok) {
                showToast('Device unblocked', 'success');
                await Promise.all([fetchActivityLogs(), fetchDevices()]);
            } else {
                const text = await res.text();
                showToast(`Unblock failed: ${text}`, 'error');
            }
        } catch {
            showToast('Network error while unblocking device', 'error');
        }
    }

    async function approveDevice(id, hostname) {
        try {
            const url = `/api/devices/approve?id=${encodeURIComponent(id)}&hostname=${encodeURIComponent(hostname || '')}`;
            const res = await fetch(url, { method: 'POST' });
            if (res.ok) {
                showToast('Device authorized', 'success');
                await Promise.all([fetchActivityLogs(), fetchDevices()]);
            }
        } catch {
            showToast('Error authorizing device', 'error');
        }
    }

    async function revokeDevice(id) {
        try {
            const url = `/api/devices/revoke?id=${encodeURIComponent(id)}`;
            const res = await fetch(url, { method: 'POST' });
            if (res.ok) {
                showToast('Authorization revoked', 'success');
                await Promise.all([fetchActivityLogs(), fetchDevices()]);
            }
        } catch {
            showToast('Error revoking authorization', 'error');
        }
    }

    function parseNetDate(dateVal) {
        if (!dateVal) return new Date();
        if (typeof dateVal === 'string' && dateVal.indexOf('/Date(') !== -1) {
            const match = /\/Date\((\d+)\)\//.exec(dateVal);
            if (match) return new Date(parseInt(match[1], 10));
        }
        return new Date(dateVal);
    }

    function formatRelativeTime(date) {
        const now = new Date();
        const diffMs = now - date;
        const diffSecs = Math.floor(diffMs / 1000);
        if (diffSecs < 10) return 'Just now';
        if (diffSecs < 60) return `${diffSecs}s ago`;
        const diffMins = Math.floor(diffSecs / 60);
        if (diffMins < 60) return `${diffMins}m ago`;
        const diffHours = Math.floor(diffMins / 60);
        if (diffHours < 24) return `${diffHours}h ago`;
        return date.toLocaleDateString();
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function showToast(message, type = 'success') {
        toast.textContent = message;
        toast.className = `toast show ${type}`;
        setTimeout(() => {
            toast.classList.remove('show');
        }, 3000);
    }

    // Start App
    init();
});
