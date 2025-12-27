const apiValues = {
    screenshot: '/api/command/screenshot',
    runTests: '/api/command/run-tests',
    report: '/api/command/generate-report',
    reset: '/api/command/reset',
    images: '/api/images',
    status: '/api/status'
};

const toastEl = document.getElementById('toast');
const galleryEl = document.getElementById('gallery');
const modal = document.getElementById('modal');
const modalImg = document.getElementById('modal-img');
const statusDot = document.getElementById('status-dot');
const statusText = document.getElementById('status-text');

let isOnline = false;

function showToast(msg, type = 'info') {
    toastEl.textContent = msg;
    toastEl.className = 'show';
    
    // Reset borders based on type
    if(type === 'error') toastEl.style.borderLeftColor = '#f44336';
    else if(type === 'success') toastEl.style.borderLeftColor = '#4caf50';
    else toastEl.style.borderLeftColor = '#2196f3';

    setTimeout(() => { toastEl.className = toastEl.className.replace('show', ''); }, 3000);
}

async function checkStatus() {
    try {
        const res = await fetch(apiValues.status);
        const data = await res.json();
        
        if (data.connected) {
            if(!isOnline) {
                // transitioned to online
                statusDot.className = 'status-dot online';
                statusText.textContent = "Unity Online";
                statusText.style.color = "#4caf50";
                isOnline = true;
            }
        } else {
            if(isOnline || statusText.textContent === "Checking...") {
                statusDot.className = 'status-dot offline';
                statusText.textContent = "Unity Offline (Press Play)";
                statusText.style.color = "#aaa";
                isOnline = false;
            }
        }
    } catch (e) {
        statusDot.className = 'status-dot offline';
        statusText.textContent = "Server Error";
        isOnline = false;
    }
}

async function sendCommand(url, name) {
    if (!isOnline && name !== "Smoke Test") { // Smoke test runs on node server
        showToast("Unity is Offline! Please Start Play Mode.", "error");
        return; 
    }

    showToast(`Executing ${name}...`);
    try {
        const res = await fetch(url);
        const data = await res.json(); 
        
        if (res.ok) {
            if (data.output) {
                // Test Result
                console.log(data.output);
                const isSuccess = data.status === 'success';
                showToast(isSuccess ? `${name} Passed!` : `${name} Failed!`, isSuccess ? 'success' : 'error');
                if(!isSuccess) alert(data.output);
            } else {
                showToast(`${name} Success!`, 'success');
            }
        } else {
            // Handle specific Unity errors
            if (data.error === 'UNITY_OFFLINE') {
                showToast("Unity is Offline!", 'error');
                checkStatus(); // Force check
            } else {
                showToast(`${name} Failed: ${data.details || res.statusText}`, 'error');
            }
        }
    } catch (err) {
        showToast(`${name} Network Error`, 'error');
    }
    
    setTimeout(refreshGallery, 1000);
}

async function refreshGallery() {
    try {
        const res = await fetch(apiValues.images);
        const images = await res.json();
        
        if (images.length === 0) {
            galleryEl.innerHTML = '<div style="color:#666; width:100%; margin-top:20px; text-align:center;">No screenshots yet. Captures will appear here.</div>';
            return;
        }

        // Only update if changed (simple optimization) to avoid flashing could be added here
        // For now, just rebuild
        galleryEl.innerHTML = '';
        images.forEach(img => {
            const div = document.createElement('div');
            div.className = 'gallery-item';
            div.onclick = () => openModal(img.url);
            
            const image = document.createElement('img');
            image.src = img.url;
            image.alt = img.name;
            
            const caption = document.createElement('div');
            caption.className = 'caption';
            
            const timeStr = new Date(img.time).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit', second:'2-digit'});
            caption.innerHTML = `<span>${timeStr}</span> <span>${img.name}</span>`;
            
            div.appendChild(image);
            div.appendChild(caption);
            galleryEl.appendChild(div);
        });
    } catch (err) {
        console.error("Failed to load images", err);
    }
}

function openModal(url) {
    modalImg.src = url;
    modal.classList.add('active');
}

// Event Listeners
document.getElementById('btn-screenshot').addEventListener('click', () => sendCommand(apiValues.screenshot, "Screenshot"));
document.getElementById('btn-run-tests').addEventListener('click', () => sendCommand(apiValues.runTests, "Smoke Test"));
document.getElementById('btn-report').addEventListener('click', async () => {
    showToast("Generating Report...");
    try {
        const res = await fetch(apiValues.report);
        const data = await res.json();
        if (data.status === 'success') {
            showToast("Opening Report...", 'success');
            window.open(data.url, '_blank');
        } else {
            showToast("Report Generation Failed", 'error');
            alert(data.output);
        }
    } catch (e) {
        showToast("Error: " + e.message, 'error');
    }
});
document.getElementById('btn-reset').addEventListener('click', () => sendCommand(apiValues.reset, "Reset Scene"));

// Initial Load & Polling
refreshGallery();
checkStatus();
setInterval(refreshGallery, 5000); // Poll images every 5s
setInterval(checkStatus, 2000);    // Poll status every 2s
