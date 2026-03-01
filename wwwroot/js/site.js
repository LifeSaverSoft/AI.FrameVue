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

    // Store the current analysis for feedback
    var currentAnalysis = null;

    // Store current file for regeneration
    var currentFile = null;

    // Store generated options and cards for comparison mode
    var generatedOptions = [null, null, null];
    var generatedCards = [null, null, null];

    // Comparison mode state
    var comparisonMode = false;
    var selectedForCompare = new Set();

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
        currentAnalysis = null;
        currentFile = null;
        generatedOptions = [null, null, null];
        generatedCards = [null, null, null];
        comparisonMode = false;
        selectedForCompare = new Set();
        hideComparisonUI();
        showSection(uploadSection);
    }

    // === User Context ===

    function getUserContext() {
        var room = document.getElementById('ctx-room');
        var wall = document.getElementById('ctx-wall');
        var decor = document.getElementById('ctx-decor');
        var purpose = document.getElementById('ctx-purpose');

        var context = {};
        if (room && room.value) context.roomType = room.value;
        if (wall && wall.value) context.wallColor = wall.value;
        if (decor && decor.value) context.decorStyle = decor.value;
        if (purpose && purpose.value) context.framePurpose = purpose.value;

        // Only return if at least one field is set
        return Object.keys(context).length > 0 ? context : null;
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

        currentFile = file;

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

        // Step 1: Analyze the artwork (two-pass: detect + expert recommendations)
        var analysis = null;
        try {
            var formData = new FormData();
            formData.append('image', file);

            // Include user context if provided
            var userContext = getUserContext();
            if (userContext) {
                formData.append('userContextJson', JSON.stringify(userContext));
            }

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

        currentAnalysis = analysis;

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
        var completedCount = 0;

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

                // Attach recommendation reasoning from analysis
                if (analysis.recommendations && analysis.recommendations[i]) {
                    option._recommendation = analysis.recommendations[i];
                }

                // Replace placeholder with the real card
                var card = createFrameCard(option, i + 1);
                resultsGrid.replaceChild(card, placeholder);

                // Store for comparison and regeneration
                generatedOptions[i] = option;
                generatedCards[i] = card;
                completedCount++;

                // Enable compare button after 2+ frames
                if (completedCount >= 2) {
                    var compareBtn = document.getElementById('compare-btn');
                    if (compareBtn) compareBtn.disabled = false;
                }

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

    // === Feedback ===

    async function submitFeedback(rating, option, tier, card) {
        var feedback = {
            artStyle: currentAnalysis ? currentAnalysis.artStyle : '',
            mood: currentAnalysis ? currentAnalysis.mood : '',
            tier: tier,
            styleName: option.styleName || '',
            rating: rating,
            wasChosen: false,
            mouldingDescription: option.products && option.products[0] ? option.products[0].description : '',
            matDescription: option.products && option.products[1] ? option.products[1].description : ''
        };

        var feedbackBtns = card.querySelector('.feedback-buttons');
        if (feedbackBtns) {
            feedbackBtns.innerHTML = '<span class="feedback-thanks">Thanks for your feedback!</span>';
        }

        try {
            await fetch('/Home/Feedback', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(feedback)
            });
        } catch (err) {
            console.warn('Feedback submission failed:', err);
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

        // "Why This Frame" reasoning from the expert knowledge base
        var reasoningHtml = '';
        if (option._recommendation && option._recommendation.reasoning) {
            reasoningHtml =
                '<div class="reasoning-section">' +
                    '<div class="reasoning-header">' +
                        '<svg viewBox="0 0 20 20" fill="none" width="14" height="14"><circle cx="10" cy="10" r="8" stroke="currentColor" stroke-width="1.5"/><path d="M10 6v5M10 13v1" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' +
                        ' Why This Frame' +
                    '</div>' +
                    renderStructuredReasoning(option._recommendation.reasoning) +
                '</div>';
        }

        var tierNames = ['Good', 'Better', 'Best'];
        var tier = tierNames[number - 1] || '';

        var hasProducts = option.products && option.products.length > 0;

        card.innerHTML =
            '<div class="frame-card-header">' +
                '<span class="style-badge">' + esc(option.styleName) + '</span>' +
                '<div class="header-right">' +
                    '<button class="btn-regenerate" type="button" title="Regenerate this option" data-index="' + (number - 1) + '">' +
                        '<svg viewBox="0 0 16 16" fill="none" width="14" height="14"><path d="M13.5 2.5v4h-4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/><path d="M2.5 8a5.5 5.5 0 019.37-3.87L13.5 6.5M2.5 13.5v-4h4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/><path d="M13.5 8A5.5 5.5 0 014.13 11.87L2.5 9.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>' +
                    '</button>' +
                    '<span class="style-number">Option ' + number + '</span>' +
                '</div>' +
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
            reasoningHtml +
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
                '<div class="feedback-buttons">' +
                    '<span class="feedback-label">How is this recommendation?</span>' +
                    '<button class="btn-feedback btn-thumbs-up" type="button" title="Good recommendation">' +
                        '<svg viewBox="0 0 20 20" fill="none" width="16" height="16"><path d="M7 10V17H4a1 1 0 01-1-1v-5a1 1 0 011-1h3zm0 0l2.5-5.5a2 2 0 013.5 1V8h3.38a2 2 0 011.96 2.36l-1 5A2 2 0 0115.38 17H7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>' +
                    '</button>' +
                    '<button class="btn-feedback btn-thumbs-down" type="button" title="Needs improvement">' +
                        '<svg viewBox="0 0 20 20" fill="none" width="16" height="16"><path d="M13 10V3h3a1 1 0 011 1v5a1 1 0 01-1 1h-3zm0 0l-2.5 5.5a2 2 0 01-3.5-1V12H3.62a2 2 0 01-1.96-2.36l1-5A2 2 0 014.62 3H13" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>' +
                    '</button>' +
                '</div>' +
            '</div>';

        // Regenerate button
        var regenBtn = card.querySelector('.btn-regenerate');
        if (regenBtn) {
            regenBtn.addEventListener('click', function() {
                var idx = parseInt(this.dataset.index);
                this.classList.add('spinning');
                regenerateOption(idx);
            });
        }

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

        // Feedback buttons
        var thumbsUp = card.querySelector('.btn-thumbs-up');
        var thumbsDown = card.querySelector('.btn-thumbs-down');
        if (thumbsUp) {
            thumbsUp.addEventListener('click', function () {
                submitFeedback('up', option, tier, card);
            });
        }
        if (thumbsDown) {
            thumbsDown.addEventListener('click', function () {
                submitFeedback('down', option, tier, card);
            });
        }

        return card;
    }

    // === Regenerate Single Option ===

    async function regenerateOption(styleIndex) {
        if (!currentFile || !currentAnalysis) return;
        var cardIndex = styleIndex; // 0-based
        var displayName = defaultTierNames[cardIndex];
        if (currentAnalysis.recommendations && currentAnalysis.recommendations[cardIndex]) {
            displayName = currentAnalysis.recommendations[cardIndex].tierName || displayName;
        }

        // Replace card with loading placeholder
        var placeholder = createPlaceholderCard(displayName, cardIndex + 1);
        var oldCard = generatedCards[cardIndex];
        if (oldCard && oldCard.parentNode) {
            oldCard.parentNode.replaceChild(placeholder, oldCard);
        }

        try {
            var formData = new FormData();
            formData.append('image', currentFile);
            formData.append('styleIndex', cardIndex);
            formData.append('analysisJson', JSON.stringify(currentAnalysis));

            var response = await fetch('/Home/FrameOne', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                placeholder.querySelector('.placeholder-content').innerHTML =
                    '<p style="color:var(--error);">Failed to regenerate.</p>' +
                    '<button class="btn-card-action" onclick="regenerateOption(' + cardIndex + ')" style="margin-top:0.5rem;">Retry</button>';
                return;
            }

            var option = await response.json();
            if (currentAnalysis.recommendations && currentAnalysis.recommendations[cardIndex]) {
                option._recommendation = currentAnalysis.recommendations[cardIndex];
            }

            var card = createFrameCard(option, cardIndex + 1);
            placeholder.parentNode.replaceChild(card, placeholder);
            generatedOptions[cardIndex] = option;
            generatedCards[cardIndex] = card;

        } catch (err) {
            console.error('Regeneration failed for style ' + cardIndex + ':', err);
            placeholder.querySelector('.placeholder-content').innerHTML =
                '<p style="color:var(--error);">Failed to regenerate.</p>' +
                '<button class="btn-card-action" onclick="regenerateOption(' + cardIndex + ')" style="margin-top:0.5rem;">Retry</button>';
        }
    }

    // === Structured Reasoning Parser ===

    function parseStructuredReasoning(text) {
        if (!text) return null;
        // Check if it contains the pipe-delimited format
        var categories = ['COLOR', 'STYLE', 'MAT', 'BALANCE', 'RULES'];
        var hasFormat = categories.some(function(cat) {
            return text.indexOf(cat + ':') >= 0;
        });
        if (!hasFormat) return null;

        var parts = text.split('|').map(function(s) { return s.trim(); });
        var result = {};
        parts.forEach(function(part) {
            var colonIdx = part.indexOf(':');
            if (colonIdx > 0) {
                var label = part.substring(0, colonIdx).trim().toUpperCase();
                var value = part.substring(colonIdx + 1).trim();
                if (categories.indexOf(label) >= 0 && value) {
                    result[label] = value;
                }
            }
        });

        return Object.keys(result).length > 0 ? result : null;
    }

    function renderStructuredReasoning(reasoning) {
        var parsed = parseStructuredReasoning(reasoning);
        if (!parsed) {
            // Fallback: render as plain paragraph
            return '<p class="reasoning-text">' + esc(reasoning) + '</p>';
        }

        var categoryLabels = {
            'COLOR': 'Color Match',
            'STYLE': 'Style Match',
            'MAT': 'Mat Selection',
            'BALANCE': 'Overall Balance',
            'RULES': 'Expert Rules Applied'
        };

        var html = '<div class="reasoning-structured">';
        Object.keys(parsed).forEach(function(key) {
            var label = categoryLabels[key] || key;
            var isRules = key === 'RULES';
            html += '<div class="reasoning-category">' +
                '<span class="reasoning-cat-label">' + esc(label) + '</span>' +
                '<span class="' + (isRules ? 'reasoning-rules' : 'reasoning-cat-text') + '">' + esc(parsed[key]) + '</span>' +
            '</div>';
        });
        html += '</div>';
        return html;
    }

    // === Comparison Mode ===

    function toggleComparisonMode() {
        comparisonMode = !comparisonMode;
        var selectionBar = document.getElementById('compare-selection-bar');
        var compareBtn = document.getElementById('compare-btn');
        var comparisonView = document.getElementById('comparison-view');

        if (comparisonMode) {
            if (compareBtn) compareBtn.textContent = 'Cancel Compare';
            selectedForCompare = new Set();
            if (selectionBar) {
                selectionBar.classList.remove('hidden');
                updateCompareSelectionBar();
            }
            if (comparisonView) comparisonView.classList.add('hidden');
        } else {
            if (compareBtn) compareBtn.textContent = 'Compare';
            selectedForCompare = new Set();
            if (selectionBar) selectionBar.classList.add('hidden');
            if (comparisonView) comparisonView.classList.add('hidden');
            resultsGrid.classList.remove('hidden');
        }
    }

    function updateCompareSelectionBar() {
        var bar = document.getElementById('compare-selection-bar');
        if (!bar) return;

        var chipsHtml = '';
        for (var i = 0; i < 3; i++) {
            if (!generatedOptions[i]) continue;
            var name = generatedOptions[i].styleName || defaultTierNames[i];
            var checked = selectedForCompare.has(i) ? ' checked' : '';
            chipsHtml +=
                '<label class="compare-chip">' +
                    '<input type="checkbox" value="' + i + '"' + checked + ' />' +
                    '<span class="compare-chip-label">' + esc(name) + '</span>' +
                '</label>';
        }

        var showEnabled = selectedForCompare.size >= 2 ? '' : ' disabled';
        bar.innerHTML = chipsHtml +
            '<button class="btn-primary compare-show-btn"' + showEnabled + ' id="show-comparison-btn">Show Comparison</button>';

        bar.querySelectorAll('input[type="checkbox"]').forEach(function(cb) {
            cb.addEventListener('change', function() {
                var idx = parseInt(this.value);
                if (this.checked) selectedForCompare.add(idx);
                else selectedForCompare.delete(idx);
                updateCompareSelectionBar();
            });
        });

        var showBtn = bar.querySelector('#show-comparison-btn');
        if (showBtn) {
            showBtn.addEventListener('click', showComparison);
        }
    }

    function showComparison() {
        if (selectedForCompare.size < 2) return;

        var comparisonView = document.getElementById('comparison-view');
        if (!comparisonView) return;

        resultsGrid.classList.add('hidden');

        var indices = Array.from(selectedForCompare).sort();
        var count = indices.length;
        var gridClass = count === 2 ? 'compare-grid-2' : 'compare-grid-3';

        var html = '<div class="' + gridClass + '">';

        // Header row
        indices.forEach(function(idx) {
            var opt = generatedOptions[idx];
            var name = opt ? opt.styleName : defaultTierNames[idx];
            html += '<div class="compare-cell compare-header"><span class="style-badge">' + esc(name) + '</span><span class="style-number">Option ' + (idx + 1) + '</span></div>';
        });

        // Image row
        indices.forEach(function(idx) {
            var opt = generatedOptions[idx];
            if (opt && opt.framedImageBase64) {
                html += '<div class="compare-cell compare-image"><img src="data:image/png;base64,' + opt.framedImageBase64 + '" alt="' + esc(opt.styleName) + '" /></div>';
            } else {
                html += '<div class="compare-cell compare-image"><div class="compare-no-image">No image</div></div>';
            }
        });

        // Reasoning row
        var reasonings = indices.map(function(idx) {
            var opt = generatedOptions[idx];
            return opt && opt._recommendation ? opt._recommendation.reasoning : '';
        });
        indices.forEach(function(idx, i) {
            html += '<div class="compare-cell compare-reasoning">' +
                '<div class="compare-cell-label">Why This Frame</div>' +
                renderStructuredReasoning(reasonings[i]) +
            '</div>';
        });

        // Products row
        indices.forEach(function(idx) {
            var opt = generatedOptions[idx];
            html += '<div class="compare-cell compare-products">';
            html += '<div class="compare-cell-label">Design Details</div>';
            if (opt && opt.products) {
                opt.products.forEach(function(p) {
                    html += '<div class="product-item">' +
                        '<div class="product-type-label">' + esc(p.type) + '</div>' +
                        '<div class="product-vendor-name">' + esc(p.vendor) + '</div>' +
                        '<div class="product-line">' + esc(p.product) + '</div>' +
                        (p.finish ? '<div class="product-finish">' + esc(p.finish) + '</div>' : '') +
                    '</div>';
                });
            }
            html += '</div>';
        });

        // Highlight differences
        html += '</div>';
        html += '<div style="text-align:center; margin-top:1.5rem;">' +
            '<button class="btn-secondary" id="exit-compare-btn">Exit Compare</button>' +
        '</div>';

        comparisonView.innerHTML = html;
        comparisonView.classList.remove('hidden');

        document.getElementById('exit-compare-btn').addEventListener('click', function() {
            comparisonView.classList.add('hidden');
            resultsGrid.classList.remove('hidden');
            comparisonMode = false;
            var compareBtn = document.getElementById('compare-btn');
            if (compareBtn) compareBtn.textContent = 'Compare';
            var selBar = document.getElementById('compare-selection-bar');
            if (selBar) selBar.classList.add('hidden');
        });
    }

    function hideComparisonUI() {
        var selectionBar = document.getElementById('compare-selection-bar');
        var comparisonView = document.getElementById('comparison-view');
        if (selectionBar) selectionBar.classList.add('hidden');
        if (comparisonView) comparisonView.classList.add('hidden');
        resultsGrid.classList.remove('hidden');
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
