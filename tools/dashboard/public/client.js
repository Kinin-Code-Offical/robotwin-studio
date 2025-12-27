const apiValues = {
    screenshot: '/api/command/screenshot',
    runTests: '/api/command/run-tests',
    report: '/api/command/generate-report',
    reset: '/api/command/reset',
    images: '/api/images'
};

const statusEl = document.getElementById('status-message');
const galleryEl = document.getElementById('gallery');
const modal = document.getElementById('modal');
const modalImg = document.getElementById('modal-img');

function setStatus(msg) {
    statusEl.textContent = msg;
    // Clear after 3 seconds
    setTimeout(() => {
        if (statusEl.textContent === msg) {
            statusEl.textContent = 'Ready.';
        }
    }, 3000);
}

async function sendCommand(url, name) {
    setStatus(`Executing ${name}...`);
    try {
        const res = await fetch(url);
        const data = await res.json(); // Treat everything as JSON now
        
        if (res.ok) {
            if (data.output) {
                // It's a test run result
                console.log(data.output);
                setStatus(data.status === 'success' ? 'Tests Passed!' : 'Tests Failed!');
                alert(data.output); // Simple output for now
            } else {
                setStatus(`${name} Success!`);
            }
        } else {
            setStatus(`${name} Failed: ${res.statusText}`);
        }
    } catch (err) {
        setStatus(`${name} Error: ${err.message}`);
    }
    // Refresh gallery immediately after a command (especially for screenshots)
    setTimeout(refreshGallery, 500);
    setTimeout(refreshGallery, 2000);
}

async function refreshGallery() {
    try {
        const res = await fetch(apiValues.images);
        const images = await res.json();
        
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
            caption.textContent = new Date(img.time).toLocaleTimeString() + " - " + img.name;
            
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
    setStatus("Generating Report...");
    try {
        const res = await fetch(apiValues.report);
        const data = await res.json();
        if (data.status === 'success') {
            setStatus("Report Ready!");
            window.open(data.url, '_blank');
        } else {
            setStatus("Report Failed!");
            alert(data.output);
        }
    } catch (e) {
        setStatus("Error: " + e.message);
    }
});
document.getElementById('btn-reset').addEventListener('click', () => sendCommand(apiValues.reset, "Reset Scene"));

// Initial Load & Polling
refreshGallery();
setInterval(refreshGallery, 3000); // Poll every 3 seconds
