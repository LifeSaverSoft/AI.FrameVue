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

    // Fallback tier names if analysis doesn't provide them
    var defaultTierNames = ['Option 1', 'Option 2', 'Option 3'];

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

        // Show loading section for analysis, then results
        showSection(loadingSection);
        analyzeAndGenerate(file);
    }

    // === Two-step process: Analyze first, then generate frames ===

    async function analyzeAndGenerate(file) {
        var analysisStep = document.getElementById('step-analysis');
        if (analysisStep) analysisStep.classList.add('active');

        // Step 1: Analyze the artwork (GPT-4o mini — fast & cheap)
        var analysis = null;
        try {
            var formData = new FormData();
            formData.append('image', file);

            var response = await fetch('/Home/Analyze', {
                method: 'POST',
                body: formData
            });

            if (response.ok) {
                analysis = await response.json();
                console.log('Artwork analysis:', analysis);
            } else {
                console.warn('Analysis failed, proceeding with empty analysis');
                analysis = { artStyle: '', dominantColors: [], mood: '', recommendations: [] };
            }
        } catch (err) {
            console.warn('Analysis error, proceeding with empty analysis:', err);
            analysis = { artStyle: '', dominantColors: [], mood: '', recommendations: [] };
        }

        if (analysisStep) analysisStep.classList.remove('active');
        if (analysisStep) analysisStep.classList.add('done');

        // Extract tier names from analysis recommendations
        var tierNames = [];
        if (analysis.recommendations && analysis.recommendations.length > 0) {
            tierNames = analysis.recommendations.map(function (r) {
                return r.tierName || r.tier || '';
            });
        }
        // Ensure we have 3 names
        while (tierNames.length < 3) {
            tierNames.push(defaultTierNames[tierNames.length] || 'Option');
        }

        // Update the loading progress steps with the tier names
        var stepEls = [
            document.getElementById('step-1'),
            document.getElementById('step-2'),
            document.getElementById('step-3')
        ];
        for (var s = 0; s < stepEls.length; s++) {
            if (stepEls[s] && tierNames[s]) {
                stepEls[s].innerHTML = '<span class="step-num">' + (s + 1) + '</span> ' + esc(tierNames[s]);
            }
        }

        // Step 2: Generate frames sequentially using analysis results
        resultsGrid.innerHTML = '';
        showSection(resultsSection);
        generateFramesSequentially(file, analysis, tierNames);
    }

    // === Generate frames one at a time, passing analysis ===

    async function generateFramesSequentially(file, analysis, tierNames) {
        var analysisJson = JSON.stringify(analysis);

        for (var i = 0; i < 3; i++) {
            var displayName = tierNames[i] || defaultTierNames[i];

            // Add a loading placeholder card
            var placeholder = createPlaceholderCard(displayName, i + 1);
            resultsGrid.appendChild(placeholder);

            try {
                var formData = new FormData();
                formData.append('image', file);
                formData.append('styleIndex', i);
                formData.append('analysisJson', analysisJson);

                var response = await fetch('/Home/FrameOne', {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) {
                    placeholder.querySelector('.placeholder-content').innerHTML =
                        '<p style="color:var(--error);">Failed to generate this option.</p>';
                    continue;
                }

                var option = await response.json();

                // Replace placeholder with the real card
                var card = createFrameCard(option, i + 1);
                resultsGrid.replaceChild(card, placeholder);

            } catch (err) {
                console.error('Frame generation failed for style ' + i + ':', err);
                placeholder.querySelector('.placeholder-content').innerHTML =
                    '<p style="color:var(--error);">Failed to generate this option.</p>';
            }
        }
    }

    // === Placeholder card ===

    function createPlaceholderCard(styleName, number) {
        var card = document.createElement('div');
        card.className = 'frame-card';
        card.innerHTML =
            '<div class="frame-card-header">' +
                '<span class="style-badge">' + esc(styleName) + '</span>' +
                '<span class="style-number">Option ' + number + '</span>' +
            '</div>' +
            '<div class="placeholder-content" style="padding:3rem;text-align:center;">' +
                '<div class="spinner" style="margin:0 auto 1rem;"><div class="spinner-frame"></div></div>' +
                '<p style="color:var(--text-secondary);font-size:0.9rem;">Designing ' + esc(styleName) + '<span class="loading-dots"><span>.</span><span>.</span><span>.</span></span></p>' +
            '</div>';
        return card;
    }

    // === Async vendor sourcing ===

    async function sourceVendorProducts(products, card) {
        var sourceBtn = card.querySelector('.btn-source');
        if (!sourceBtn) return;

        // Replace button with loading indicator
        var statusEl = document.createElement('div');
        statusEl.className = 'vendor-sourcing-status';
        statusEl.innerHTML = 'Sourcing<span class="loading-dots"><span>.</span><span>.</span><span>.</span></span>';
        sourceBtn.replaceWith(statusEl);

        try {
            var response = await fetch('/Home/SourceProducts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(products)
            });

            if (!response.ok) {
                statusEl.textContent = 'Could not source products.';
                statusEl.classList.add('sourcing-error');
                return;
            }

            var sourced = await response.json();

            // Update the product items with item numbers
            var productItems = card.querySelectorAll('.product-item');
            sourced.forEach(function (p, i) {
                if (i < productItems.length) {
                    var itemEl = productItems[i];

                    if (p.product) {
                        var lineEl = itemEl.querySelector('.product-line');
                        if (lineEl) lineEl.textContent = p.product;
                    }

                    if (p.itemNumber) {
                        var numEl = itemEl.querySelector('.product-item-number');
                        if (numEl) {
                            numEl.textContent = 'Item #' + p.itemNumber;
                            numEl.classList.remove('hidden');
                        }
                    }
                }
            });

            statusEl.textContent = 'Products sourced';
            statusEl.classList.add('sourcing-done');

        } catch (err) {
            console.error('Vendor sourcing failed:', err);
            statusEl.textContent = 'Could not source products.';
            statusEl.classList.add('sourcing-error');
        }
    }

    // === Frame card ===

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
                    '<div class="product-item-number hidden"></div>' +
                    (p.finish ? '<div class="product-finish">' + esc(p.finish) + '</div>' : '') +
                    (p.description ? '<div class="product-desc">' + esc(p.description) + '</div>' : '') +
                '</div>'
            ).join('');
        }

        var hasProducts = option.products && option.products.length > 0;

        card.innerHTML =
            '<div class="frame-card-header">' +
                '<span class="style-badge">' + esc(option.styleName) + '</span>' +
                '<span class="style-number">Option ' + number + '</span>' +
            '</div>' +
            (imageSrc
                ? '<div class="frame-image-container" data-src="' + imageSrc + '" data-name="' + esc(option.styleName) + '">' +
                      '<img src="' + imageSrc + '" alt="' + esc(option.styleName) + ' framing" loading="lazy" />' +
                      '<div class="image-overlay">Tap to enlarge</div>' +
                  '</div>'
                : '<div class="frame-image-container" style="padding:3rem;text-align:center;color:var(--text-muted);">' +
                      'Image not available' +
                  '</div>'
            ) +
            '<div class="product-details">' +
                '<h4>Design Details</h4>' +
                (productsHtml || '<p style="color:var(--text-muted);font-size:0.85rem;">Design details not available</p>') +
                '<div class="card-actions">' +
                    (hasProducts
                        ? '<button class="btn-card-action btn-source" type="button">' +
                              '<svg viewBox="0 0 20 20" fill="none" width="14" height="14"><path d="M10 2v6m0 0l3-3m-3 3L7 5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/><path d="M3 10v5a2 2 0 002 2h10a2 2 0 002-2v-5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' +
                              'Source Products' +
                          '</button>'
                        : '') +
                    (imageSrc
                        ? '<button class="btn-card-action btn-wall" type="button">' +
                              '<svg viewBox="0 0 20 20" fill="none" width="14" height="14"><rect x="2" y="4" width="16" height="12" rx="1.5" stroke="currentColor" stroke-width="1.5"/><rect x="6" y="7" width="8" height="6" stroke="currentColor" stroke-width="1" opacity="0.5"/><path d="M5 2v2M15 2v2M10 2v2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' +
                              'Preview on Wall' +
                          '</button>'
                        : '') +
                '</div>' +
            '</div>';

        // Lightbox on image click/tap
        const imgContainer = card.querySelector('.frame-image-container[data-src]');
        if (imgContainer) {
            imgContainer.addEventListener('click', function () {
                openLightbox(this.dataset.src, this.dataset.name);
            });
        }

        // Source Products button
        var sourceBtn = card.querySelector('.btn-source');
        if (sourceBtn) {
            sourceBtn.addEventListener('click', function () {
                sourceVendorProducts(option.products, card);
            });
        }

        // Preview on Wall button
        var wallBtn = card.querySelector('.btn-wall');
        if (wallBtn) {
            wallBtn.addEventListener('click', function () {
                openWallViewer(imageSrc, option.styleName);
            });
        }

        return card;
    }

    // === Lightbox ===

    function openLightbox(src, styleName) {
        const overlay = document.createElement('div');
        overlay.className = 'lightbox';

        overlay.innerHTML =
            '<div class="lightbox-label">' + esc(styleName || '') + '</div>' +
            '<button class="lightbox-close" type="button">&times;</button>' +
            '<div class="lightbox-img-wrap">' +
                '<img src="' + src + '" alt="' + esc(styleName || 'Framed image') + '" />' +
            '</div>' +
            '<div class="lightbox-footer">' +
                '<button class="lightbox-action-btn" type="button">' +
                    '<svg viewBox="0 0 20 20" fill="none" width="16" height="16"><rect x="2" y="4" width="16" height="12" rx="1.5" stroke="currentColor" stroke-width="1.5"/><rect x="6" y="7" width="8" height="6" stroke="currentColor" stroke-width="1" opacity="0.5"/><path d="M5 2v2M15 2v2M10 2v2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' +
                    'Preview on Wall' +
                '</button>' +
            '</div>' +
            '<span class="lightbox-hint">Esc or click backdrop to close</span>';

        function close() {
            overlay.remove();
            document.removeEventListener('keydown', onKey);
        }

        function onKey(e) {
            if (e.key === 'Escape') close();
        }

        // Close on backdrop click (not on image or buttons)
        overlay.querySelector('.lightbox-img-wrap').addEventListener('click', function (e) {
            if (e.target === this) close();
        });
        overlay.querySelector('.lightbox-close').addEventListener('click', close);

        // Wall preview from lightbox
        overlay.querySelector('.lightbox-action-btn').addEventListener('click', function () {
            close();
            openWallViewer(src, styleName);
        });

        document.addEventListener('keydown', onKey);
        document.body.appendChild(overlay);
    }

    // === Wall Viewer Modal ===

    function openWallViewer(framedSrc, styleName) {
        var modal = document.createElement('div');
        modal.className = 'wall-modal';

        modal.innerHTML =
            '<div class="wall-modal-panel">' +
                '<div class="wall-modal-header">' +
                    '<div>' +
                        '<div class="wall-modal-title">Preview on Your Wall</div>' +
                        '<div class="wall-modal-subtitle">Upload or snap a photo of your wall and see how <strong>' + esc(styleName) + '</strong> looks in your space.</div>' +
                    '</div>' +
                    '<button class="wall-modal-close" type="button">&times;</button>' +
                '</div>' +
                '<div class="wall-upload-zone" id="wall-drop-zone">' +
                    '<div class="wall-upload-icon">' +
                        '<svg viewBox="0 0 48 48" fill="none" width="48" height="48">' +
                            '<rect x="4" y="8" width="40" height="32" rx="3" stroke="currentColor" stroke-width="2"/>' +
                            '<path d="M4 32l10-10 8 8 6-6 16 16" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" opacity="0.4"/>' +
                            '<circle cx="14" cy="18" r="4" stroke="currentColor" stroke-width="2" opacity="0.4"/>' +
                        '</svg>' +
                    '</div>' +
                    '<div class="wall-upload-label">Drop a photo of your wall here</div>' +
                    '<div class="wall-upload-label">or <span class="gold">click to browse</span></div>' +
                    '<div class="wall-upload-hint">Take a photo with your phone or upload from your gallery</div>' +
                    '<input type="file" accept="image/jpeg,image/png,image/webp" capture="environment" hidden />' +
                '</div>' +
                '<div class="wall-generating hidden">' +
                    '<div class="spinner" style="margin:0 auto 1rem;"><div class="spinner-frame"></div></div>' +
                    '<div>Placing your art on the wall<span class="loading-dots"><span>.</span><span>.</span><span>.</span></span></div>' +
                    '<div class="wall-generating-hint">This may take a moment</div>' +
                '</div>' +
                '<div class="wall-result hidden"></div>' +
                '<div class="wall-error hidden"></div>' +
            '</div>';

        var panel = modal.querySelector('.wall-modal-panel');
        var dropZoneEl = modal.querySelector('#wall-drop-zone');
        var wallFileInput = dropZoneEl.querySelector('input[type="file"]');
        var generatingEl = modal.querySelector('.wall-generating');
        var resultEl = modal.querySelector('.wall-result');
        var errorEl = modal.querySelector('.wall-error');

        // Close modal
        function closeModal() {
            modal.remove();
            document.removeEventListener('keydown', onModalKey);
        }

        function onModalKey(e) {
            if (e.key === 'Escape') closeModal();
        }

        modal.addEventListener('click', function (e) {
            if (e.target === modal) closeModal();
        });
        modal.querySelector('.wall-modal-close').addEventListener('click', closeModal);
        document.addEventListener('keydown', onModalKey);

        // Drag & drop on wall upload zone
        dropZoneEl.addEventListener('dragover', function (e) {
            e.preventDefault();
            dropZoneEl.classList.add('drag-over');
        });
        dropZoneEl.addEventListener('dragleave', function () {
            dropZoneEl.classList.remove('drag-over');
        });
        dropZoneEl.addEventListener('drop', function (e) {
            e.preventDefault();
            dropZoneEl.classList.remove('drag-over');
            if (e.dataTransfer.files.length > 0) {
                handleWallPhoto(e.dataTransfer.files[0]);
            }
        });
        dropZoneEl.addEventListener('click', function () {
            wallFileInput.click();
        });
        wallFileInput.addEventListener('change', function () {
            if (wallFileInput.files.length > 0) {
                handleWallPhoto(wallFileInput.files[0]);
            }
        });

        // Handle wall photo upload
        async function handleWallPhoto(file) {
            if (!file.type.startsWith('image/')) return;

            dropZoneEl.classList.add('hidden');
            errorEl.classList.add('hidden');
            resultEl.classList.add('hidden');
            generatingEl.classList.remove('hidden');

            try {
                var formData = new FormData();
                formData.append('wallPhoto', file);
                formData.append('framedImageBase64', framedSrc.replace(/^data:image\/\w+;base64,/, ''));

                var response = await fetch('/Home/WallPreview', {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) {
                    throw new Error('Server returned ' + response.status);
                }

                var data = await response.json();
                generatingEl.classList.add('hidden');

                if (data.previewImageBase64) {
                    resultEl.innerHTML =
                        '<img src="data:image/png;base64,' + data.previewImageBase64 + '" alt="Your art on the wall" />' +
                        '<div class="wall-result-actions">' +
                            '<button class="btn-secondary" type="button">Try Another Photo</button>' +
                        '</div>';
                    resultEl.classList.remove('hidden');

                    resultEl.querySelector('.btn-secondary').addEventListener('click', function () {
                        resultEl.classList.add('hidden');
                        wallFileInput.value = '';
                        dropZoneEl.classList.remove('hidden');
                    });
                } else {
                    throw new Error('No preview image returned');
                }

            } catch (err) {
                console.error('Wall preview failed:', err);
                generatingEl.classList.add('hidden');
                errorEl.innerHTML =
                    '<p>Could not generate the wall preview. Please try again.</p>' +
                    '<button class="btn-secondary" type="button">Try Again</button>';
                errorEl.classList.remove('hidden');

                errorEl.querySelector('.btn-secondary').addEventListener('click', function () {
                    errorEl.classList.add('hidden');
                    wallFileInput.value = '';
                    dropZoneEl.classList.remove('hidden');
                });
            }
        }

        document.body.appendChild(modal);
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
