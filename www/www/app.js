document.addEventListener('DOMContentLoaded', () => {
    // State
    let selfInfo = null;
    let peers = [];
    let selectedPrinter = null; // { peer, printerName }
    let selectedFile = null;

    // DOM Elements
    const statusText = document.getElementById('local-status');
    const printerList = document.getElementById('printer-list');
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    const selectedFileContainer = document.getElementById('selected-file');
    const filenameDisplay = document.getElementById('filename-display');
    const removeFileBtn = document.getElementById('remove-file-btn');
    const printBtn = document.getElementById('print-btn');
    const toast = document.getElementById('toast');

    // Initialize
    async function init() {
        try {
            await fetchSelf();
            await fetchPeers();
            // Poll for peers every 5 seconds
            setInterval(fetchPeers, 5000);
        } catch (error) {
            showToast('Failed to connect to local PrintHop service.', 'error');
            statusText.textContent = 'Disconnected';
            statusText.previousElementSibling.classList.remove('online');
            statusText.previousElementSibling.style.backgroundColor = 'var(--danger)';
        }
    }

    async function fetchSelf() {
        const res = await fetch('/api/self');
        if (!res.ok) throw new Error('Network error');
        selfInfo = await res.json();
        statusText.textContent = `Online as ${selfInfo.hostname}`;
    }

    async function fetchPeers() {
        try {
            const res = await fetch('/api/peers');
            if (!res.ok) return;
            const newPeers = await res.json();
            
            // Also add self to the list of peers to print locally
            const allPeers = [selfInfo, ...newPeers];
            
            renderPrinters(allPeers);
        } catch (error) {
            console.error('Failed to fetch peers:', error);
        }
    }

    function renderPrinters(peerList) {
        if (peerList.length === 0) {
            printerList.innerHTML = '<div class="loading">No printers found.</div>';
            return;
        }

        printerList.innerHTML = '';
        
        peerList.forEach(peer => {
            if (!peer.printers || peer.printers.length === 0) return;
            
            peer.printers.forEach(printer => {
                const card = document.createElement('div');
                card.className = 'printer-card';
                
                // Check if this is the currently selected printer
                const isSelected = selectedPrinter && 
                                   selectedPrinter.peer.id === peer.id && 
                                   selectedPrinter.printerName === printer;
                
                if (isSelected) {
                    card.classList.add('selected');
                }

                card.innerHTML = `
                    <div class="machine-name">${peer.hostname} ${peer.id === selfInfo.id ? '(Local)' : ''}</div>
                    <div class="printer-name">${printer}</div>
                `;

                card.addEventListener('click', () => {
                    document.querySelectorAll('.printer-card').forEach(c => c.classList.remove('selected'));
                    card.classList.add('selected');
                    selectedPrinter = { peer, printerName: printer };
                    updatePrintButton();
                });

                printerList.appendChild(card);
            });
        });
        
        if (printerList.innerHTML === '') {
            printerList.innerHTML = '<div class="loading">No printers available.</div>';
        }
    }

    // File Handling
    dropZone.addEventListener('click', () => fileInput.click());
    
    dropZone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropZone.classList.add('dragover');
    });

    dropZone.addEventListener('dragleave', () => {
        dropZone.classList.remove('dragover');
    });

    dropZone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropZone.classList.remove('dragover');
        
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
        selectedFile = file;
        filenameDisplay.textContent = file.name;
        selectedFileContainer.classList.remove('hidden');
        dropZone.style.display = 'none';
        updatePrintButton();
    }

    function updatePrintButton() {
        printBtn.disabled = !(selectedPrinter && selectedFile);
    }

    // Print Action
    printBtn.addEventListener('click', async () => {
        if (!selectedPrinter || !selectedFile || !selfInfo) return;

        printBtn.disabled = true;
        printBtn.textContent = 'Sending...';

        const formData = new FormData();
        formData.append('senderId', selfInfo.id);
        formData.append('senderHostname', selfInfo.hostname);
        formData.append('printerName', selectedPrinter.printerName);
        formData.append('file', selectedFile);

        try {
            // Send directly to the target machine's IP
            const targetUrl = `http://${selectedPrinter.peer.ip}:${selectedPrinter.peer.httpPort}/api/receive-print`;
            
            const res = await fetch(targetUrl, {
                method: 'POST',
                body: formData
            });

            if (res.ok) {
                showToast('Print job sent successfully!', 'success');
                // Reset file
                removeFileBtn.click();
            } else {
                const text = await res.text();
                showToast(`Failed: ${text}`, 'error');
            }
        } catch (error) {
            showToast('Network error while sending print job.', 'error');
            console.error(error);
        } finally {
            printBtn.textContent = 'Print Document';
            updatePrintButton();
        }
    });

    function showToast(message, type = 'success') {
        toast.textContent = message;
        toast.className = `toast show ${type}`;
        
        setTimeout(() => {
            toast.classList.remove('show');
        }, 3000);
    }

    // Boot
    init();
});
