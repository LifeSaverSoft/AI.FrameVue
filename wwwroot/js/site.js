// ===== AI.FrameVue — Frontend Logic =====

(function () {
    'use strict';

    // DOM elements
    const dropZone = document.getElementById('drop-zone');
    const fileInput = document.getElementById('file-input');
    const uploadSection = document.getElementById('upload-section');
    const loadingSection = document.getElementById('loading-section');
    const resultsSection = document.getElementById('results-section');
    const errorSection = document.getElementById('error-section');
    const previewImage = document.getElementById('preview-image');
    const resultsGrid = document.getElementById('results-grid');
    const errorMessage = document.getElementById('error-message');
    const newUploadBtn = document.getElementById('new-upload-btn');
    const retryBtn = document.getElementById('retry-btn');

    // === Section visibility ===

    function showSection(section) {
        [uploadSection, loadingSection, resultsSection, errorSection]
            .forEach(s => s.classList.add('hidden'));
        section.classList.remove('hidden');
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    // === Drag & Drop ===

    dropZone.addEventListener('dragover', e => {
        e.preventDefault();
        dropZone.classList.add('drag-over');
    });

    dropZone.addEventListener('dragleave', () => {
        dropZone.classList.remove('drag-over');
    });

    dropZone.addEventListener('drop', e => {
        e.preventDefault();
        dropZone.classList.remove('drag-over');
        if (e.dataTransfer.files.length > 0) {
            handleFile(e.dataTransfer.files[0]);
        }
    });

    dropZone.addEventListener('click', () => fileInput.click());

    fileInput.addEventListener('change', () => {
        if (fileInput.files.length > 0) {
            handleFile(fileInput.files[0]);
        }
    });

    // === Buttons ===

    newUploadBtn.addEventListener('click', resetToUpload);
    retryBtn.addEventListener('click', resetToUpload);

    function resetToUpload() {
        fileInput.value = '';
        resultsGrid.innerHTML = '';
        showSection(uploadSection);
    }

    // === File handling ===

    function handleFile(file) {
        if (!file.type.startsWith('image/')) {
            showError('Please upload a valid image file (JPG, PNG, or WebP).');
            return;
        }

        if (file.size > 20 * 1024 * 1024) {
            showError('Image must be under 20 MB.');
            return;
        }

        // Show preview
        const reader = new FileReader();
        reader.onload = e => {
            previewImage.src = e.target.result;
        };
        reader.readAsDataURL(file);

        // Show loading
        showSection(loadingSection);
        animateSteps();

        // Upload
        uploadImage(file);
    }

    // === Loading step animation ===

    function animateSteps() {
        const steps = [
            document.getElementById('step-1'),
            document.getElementById('step-2'),
            document.getElementById('step-3')
        ];

        steps.forEach(s => s.classList.remove('active', 'done'));

        let current = 0;

        function tick() {
            if (current > 0 && current <= steps.length) {
                steps[current - 1].classList.remove('active');
                steps[current - 1].classList.add('done');
            }
            if (current < steps.length) {
                steps[current].classList.add('active');
                current++;
            } else {
                steps.forEach(s => s.classList.remove('active', 'done'));
                current = 0;
            }
        }

        tick();
        window._stepInterval = setInterval(tick, 5000);
    }

    // === Upload ===

    async function uploadImage(file) {
        const formData = new FormData();
        formData.append('image', file);

        try {
            const response = await fetch('/Home/Upload', {
                method: 'POST',
                body: formData
            });

            clearStepInterval();

            if (!response.ok) {
                let errMsg = 'Server error. Please try again.';
                try {
                    const errData = await response.json();
                    if (errData.error) errMsg = errData.error;
                } catch (_) { /* ignore parse error */ }
                showError(errMsg);
                return;
            }

            const data = await response.json();

            if (data.error) {
                showError(data.error);
                return;
            }

            renderResults(data);

        } catch (err) {
            clearStepInterval();
            console.error('Upload failed:', err);
            showError('Network error. Please check your connection and try again.');
        }
    }

    function clearStepInterval() {
        if (window._stepInterval) {
            clearInterval(window._stepInterval);
            window._stepInterval = null;
        }
    }

    // === Render results ===

    function renderResults(data) {
        resultsGrid.innerHTML = '';

        if (!data.options || data.options.length === 0) {
            showError('No framing options were returned. Please try again.');
            return;
        }

        data.options.forEach((option, index) => {
            resultsGrid.appendChild(createFrameCard(option, index + 1));
        });

        showSection(resultsSection);
    }

    function createFrameCard(option, number) {
        const card = document.createElement('div');
        card.className = 'frame-card';

        const imageSrc = option.framedImageBase64
            ? 'data:image/png;base64,' + option.framedImageBase64
            : '';

        let productsHtml = '';
        if (option.products && option.products.length > 0) {
            productsHtml = option.products.map(p =>
                '<div class="product-item">' +
                    '<div class="product-type-label">' + esc(p.type) + '</div>' +
                    '<div class="product-vendor-name">' + esc(p.vendor) + '</div>' +
                    '<div class="product-line">' + esc(p.product) + '</div>' +
                    (p.finish ? '<div class="product-finish">' + esc(p.finish) + '</div>' : '') +
                    (p.description ? '<div class="product-desc">' + esc(p.description) + '</div>' : '') +
                '</div>'
            ).join('');
        }

        card.innerHTML =
            '<div class="frame-card-header">' +
                '<span class="style-badge">' + esc(option.styleName) + '</span>' +
                '<span class="style-number">Option ' + number + '</span>' +
            '</div>' +
            (imageSrc
                ? '<div class="frame-image-container" data-src="' + imageSrc + '">' +
                      '<img src="' + imageSrc + '" alt="' + esc(option.styleName) + ' framing" loading="lazy" />' +
                      '<div class="image-overlay">Tap to enlarge</div>' +
                  '</div>'
                : '<div class="frame-image-container" style="padding:3rem;text-align:center;color:var(--text-muted);">' +
                      'Image not available' +
                  '</div>'
            ) +
            '<div class="product-details">' +
                '<h4>Products Used</h4>' +
                (productsHtml || '<p style="color:var(--text-muted);font-size:0.85rem;">Product details not available</p>') +
            '</div>';

        // Lightbox on image click/tap
        const imgContainer = card.querySelector('.frame-image-container[data-src]');
        if (imgContainer) {
            imgContainer.addEventListener('click', function () {
                openLightbox(this.dataset.src);
            });
        }

        return card;
    }

    // === Lightbox ===

    function openLightbox(src) {
        const overlay = document.createElement('div');
        overlay.className = 'lightbox';

        const img = document.createElement('img');
        img.src = src;
        img.alt = 'Enlarged framed image';
        overlay.appendChild(img);

        function close() {
            overlay.remove();
            document.removeEventListener('keydown', onKey);
        }

        function onKey(e) {
            if (e.key === 'Escape') close();
        }

        overlay.addEventListener('click', close);
        document.addEventListener('keydown', onKey);
        document.body.appendChild(overlay);
    }

    // === Error display ===

    function showError(msg) {
        errorMessage.textContent = msg;
        showSection(errorSection);
    }

    // === Utility ===

    function esc(str) {
        if (!str) return '';
        var d = document.createElement('div');
        d.textContent = str;
        return d.innerHTML;
    }

})();
