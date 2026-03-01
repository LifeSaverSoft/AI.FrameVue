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

    // Browse & Discover state
    var browseSection = document.getElementById('browse-section');
    var discoverSection = document.getElementById('discover-section');
    var artPrintPage = 1;
    var artPrintHasMore = true;
    var artPrintLoading = false;
    var discoverySelections = { room: null, mood: null, colors: [], style: null };
    var discoveryExcludeIds = [];
    var currentPrintDetail = null;

    // === Section visibility ===

    function showSection(section) {
        [uploadSection, loadingSection, resultsSection, errorSection, browseSection, discoverSection]
            .filter(Boolean)
            .forEach(s => s.classList.add('hidden'));
        if (section) section.classList.remove('hidden');
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
                (typeof THREE !== 'undefined'
                    ? '<button class="lightbox-action-btn lightbox-3d-btn" type="button">' +
                        '<svg viewBox="0 0 20 20" fill="none" width="16" height="16"><path d="M10 2l8 4v8l-8 4-8-4V6l8-4z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/><path d="M10 10v8M10 10l8-4M10 10L2 6" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>' +
                        '3D View' +
                      '</button>'
                    : '') +
                '<button class="lightbox-action-btn lightbox-wall-btn" type="button">' +
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
        overlay.querySelector('.lightbox-wall-btn').addEventListener('click', function () {
            close();
            openWallViewer(src, styleName);
        });

        // 3D view from lightbox
        var threeDBtn = overlay.querySelector('.lightbox-3d-btn');
        if (threeDBtn) {
            threeDBtn.addEventListener('click', function () {
                close();
                open3DViewer(src, styleName);
            });
        }

        document.addEventListener('keydown', onKey);
        document.body.appendChild(overlay);
    }

    // === 3D Frame Viewer (Three.js) ===

    function open3DViewer(imageSrc, styleName) {
        if (typeof THREE === 'undefined') return;

        var overlay = document.createElement('div');
        overlay.className = 'viewer3d';
        overlay.innerHTML =
            '<button class="viewer3d-close" type="button">&times;</button>' +
            '<div class="viewer3d-label">' + esc(styleName || '') + '</div>' +
            '<div class="viewer3d-canvas-wrap"></div>' +
            '<div class="viewer3d-hint">Click &amp; drag to orbit &middot; Scroll to zoom</div>' +
            '<div class="viewer3d-controls">' +
                '<div class="viewer3d-frametype">' +
                    '<span class="viewer3d-control-label">Frame</span>' +
                    '<button class="frametype-opt active" data-type="standard">Standard</button>' +
                    '<button class="frametype-opt" data-type="shadowbox">Shadow Box</button>' +
                '</div>' +
                '<div class="viewer3d-glazing">' +
                    '<span class="viewer3d-control-label">Glazing</span>' +
                    '<button class="glazing-opt active" data-glass="museum">Museum Glass</button>' +
                    '<button class="glazing-opt" data-glass="conservation">Conservation Clear</button>' +
                    '<button class="glazing-opt" data-glass="clear">Regular Clear</button>' +
                '</div>' +
            '</div>';

        document.body.appendChild(overlay);

        var canvasWrap = overlay.querySelector('.viewer3d-canvas-wrap');
        var w = canvasWrap.clientWidth;
        var h = canvasWrap.clientHeight;

        // Renderer
        var renderer = new THREE.WebGLRenderer({ antialias: true });
        renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        renderer.setSize(w, h);
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.0;
        canvasWrap.appendChild(renderer.domElement);

        // Scene
        var scene = new THREE.Scene();
        scene.background = new THREE.Color(0x08080f);

        // Camera
        var camera = new THREE.PerspectiveCamera(45, w / h, 0.1, 100);
        camera.position.set(0, 0, 3.5);

        // Controls
        var controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.08;
        controls.minDistance = 1.5;
        controls.maxDistance = 7;
        controls.maxPolarAngle = Math.PI * 0.85;
        controls.minPolarAngle = Math.PI * 0.15;

        // Lighting
        scene.add(new THREE.AmbientLight(0xffffff, 0.4));
        var keyLight = new THREE.DirectionalLight(0xffffff, 0.8);
        keyLight.position.set(2, 3, 4);
        scene.add(keyLight);
        var fillLight = new THREE.DirectionalLight(0xfff5e0, 0.3);
        fillLight.position.set(-2, 1, 2);
        scene.add(fillLight);

        // Environment map for glazing reflections
        var pmremGen = new THREE.PMREMGenerator(renderer);
        var envScene = new THREE.Scene();
        envScene.background = new THREE.Color(0x333333);
        var el1 = new THREE.DirectionalLight(0xffffff, 1);
        el1.position.set(1, 1, 1);
        envScene.add(el1);
        var el2 = new THREE.DirectionalLight(0xfff5e0, 0.5);
        el2.position.set(-1, 0.5, -0.5);
        envScene.add(el2);
        var envMap = pmremGen.fromScene(envScene, 0.04).texture;
        scene.environment = envMap;
        envScene = null;

        // Glazing presets
        var glazingPresets = {
            museum:       { transmission: 0.98, roughness: 0.02, reflectivity: 0.02, ior: 1.5 },
            conservation: { transmission: 0.92, roughness: 0.08, reflectivity: 0.15, ior: 1.5 },
            clear:        { transmission: 0.88, roughness: 0.12, reflectivity: 0.35, ior: 1.5 }
        };

        var frameGroup = new THREE.Group();
        scene.add(frameGroup);
        var glassMaterial = null;
        var currentFrameType = 'standard';
        var currentGlazing = 'museum';
        var animId = null;

        // Load texture and build frame
        var texLoader = new THREE.TextureLoader();
        texLoader.load(imageSrc, function (texture) {
            texture.colorSpace = THREE.SRGBColorSpace;
            buildFrame(texture);
        });

        function buildFrame(texture) {
            // Clear previous geometry
            while (frameGroup.children.length > 0) {
                var child = frameGroup.children[0];
                frameGroup.remove(child);
                if (child.geometry) child.geometry.dispose();
                if (child.material && child.material !== glassMaterial) {
                    child.material.dispose();
                }
            }
            if (glassMaterial) { glassMaterial.dispose(); glassMaterial = null; }

            var aspect = texture.image.width / texture.image.height;
            var imgW, imgH;
            if (aspect >= 1) { imgW = 1.2; imgH = 1.2 / aspect; }
            else { imgW = 1.2 * aspect; imgH = 1.2; }

            var matBorder = 0.08;
            var frameBorder = 0.06;
            var matW = imgW + matBorder * 2;
            var matH = imgH + matBorder * 2;
            var frameW = matW + frameBorder * 2;
            var frameH = matH + frameBorder * 2;

            var isSB = currentFrameType === 'shadowbox';
            var frameDepth = isSB ? 0.28 : 0.06;
            var imageZ = isSB ? -0.18 : 0;
            var glassZ = frameDepth / 2 + 0.001;

            // --- Image plane ---
            var imgMesh = new THREE.Mesh(
                new THREE.PlaneGeometry(imgW, imgH),
                new THREE.MeshStandardMaterial({ map: texture })
            );
            imgMesh.position.z = imageZ;
            frameGroup.add(imgMesh);

            // --- Mat (extruded ring) ---
            var matShape = new THREE.Shape();
            matShape.moveTo(-matW / 2, -matH / 2);
            matShape.lineTo(matW / 2, -matH / 2);
            matShape.lineTo(matW / 2, matH / 2);
            matShape.lineTo(-matW / 2, matH / 2);
            matShape.closePath();
            var matHole = new THREE.Path();
            matHole.moveTo(-imgW / 2, -imgH / 2);
            matHole.lineTo(-imgW / 2, imgH / 2);
            matHole.lineTo(imgW / 2, imgH / 2);
            matHole.lineTo(imgW / 2, -imgH / 2);
            matHole.closePath();
            matShape.holes.push(matHole);
            var matMesh = new THREE.Mesh(
                new THREE.ExtrudeGeometry(matShape, { depth: 0.015, bevelEnabled: false }),
                new THREE.MeshStandardMaterial({ color: 0xf5f0e8, roughness: 0.85 })
            );
            matMesh.position.z = imageZ - 0.008;
            frameGroup.add(matMesh);

            // --- Frame (extruded ring) ---
            var fShape = new THREE.Shape();
            fShape.moveTo(-frameW / 2, -frameH / 2);
            fShape.lineTo(frameW / 2, -frameH / 2);
            fShape.lineTo(frameW / 2, frameH / 2);
            fShape.lineTo(-frameW / 2, frameH / 2);
            fShape.closePath();
            var fHole = new THREE.Path();
            fHole.moveTo(-matW / 2, -matH / 2);
            fHole.lineTo(-matW / 2, matH / 2);
            fHole.lineTo(matW / 2, matH / 2);
            fHole.lineTo(matW / 2, -matH / 2);
            fHole.closePath();
            fShape.holes.push(fHole);
            var frameMesh = new THREE.Mesh(
                new THREE.ExtrudeGeometry(fShape, { depth: frameDepth, bevelEnabled: false }),
                new THREE.MeshStandardMaterial({ color: 0x3d2b1f, roughness: 0.6, metalness: 0.2 })
            );
            frameMesh.position.z = -frameDepth / 2;
            frameGroup.add(frameMesh);

            // --- Shadow box interior walls ---
            if (isSB) {
                var wallMat = new THREE.MeshStandardMaterial({ color: 0xf0ead6, roughness: 0.9 });
                var innerDepth = glassZ - imageZ;

                var topG = new THREE.PlaneGeometry(matW, innerDepth);
                var topW = new THREE.Mesh(topG, wallMat);
                topW.position.set(0, matH / 2, imageZ + innerDepth / 2);
                topW.rotation.x = Math.PI / 2;
                frameGroup.add(topW);

                var botW = new THREE.Mesh(topG.clone(), wallMat);
                botW.position.set(0, -matH / 2, imageZ + innerDepth / 2);
                botW.rotation.x = -Math.PI / 2;
                frameGroup.add(botW);

                var sideG = new THREE.PlaneGeometry(innerDepth, matH);
                var leftW = new THREE.Mesh(sideG, wallMat);
                leftW.position.set(-matW / 2, 0, imageZ + innerDepth / 2);
                leftW.rotation.y = Math.PI / 2;
                frameGroup.add(leftW);

                var rightW = new THREE.Mesh(sideG.clone(), wallMat);
                rightW.position.set(matW / 2, 0, imageZ + innerDepth / 2);
                rightW.rotation.y = -Math.PI / 2;
                frameGroup.add(rightW);
            }

            // --- Glass plane (glazing) ---
            var preset = glazingPresets[currentGlazing];
            glassMaterial = new THREE.MeshPhysicalMaterial({
                transmission: preset.transmission,
                roughness: preset.roughness,
                reflectivity: preset.reflectivity,
                ior: preset.ior,
                thickness: 0.002,
                transparent: true,
                side: THREE.FrontSide
            });
            var glassMesh = new THREE.Mesh(
                new THREE.PlaneGeometry(imgW + matBorder * 2 - 0.01, imgH + matBorder * 2 - 0.01),
                glassMaterial
            );
            glassMesh.position.z = glassZ;
            frameGroup.add(glassMesh);
        }

        // --- Glazing controls ---
        overlay.querySelectorAll('.glazing-opt').forEach(function (btn) {
            btn.addEventListener('click', function () {
                overlay.querySelectorAll('.glazing-opt').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                currentGlazing = btn.dataset.glass;
                if (glassMaterial) {
                    var p = glazingPresets[currentGlazing];
                    glassMaterial.transmission = p.transmission;
                    glassMaterial.roughness = p.roughness;
                    glassMaterial.reflectivity = p.reflectivity;
                    glassMaterial.ior = p.ior;
                    glassMaterial.needsUpdate = true;
                }
            });
        });

        // --- Frame type controls ---
        overlay.querySelectorAll('.frametype-opt').forEach(function (btn) {
            btn.addEventListener('click', function () {
                overlay.querySelectorAll('.frametype-opt').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                currentFrameType = btn.dataset.type;
                var tex = null;
                frameGroup.traverse(function (ch) {
                    if (ch.material && ch.material.map) tex = ch.material.map;
                });
                if (tex) buildFrame(tex);
            });
        });

        // --- Animation loop ---
        function animate() {
            animId = requestAnimationFrame(animate);
            controls.update();
            renderer.render(scene, camera);
        }
        animate();

        // --- Resize ---
        function onResize() {
            var cw = canvasWrap.clientWidth;
            var ch = canvasWrap.clientHeight;
            camera.aspect = cw / ch;
            camera.updateProjectionMatrix();
            renderer.setSize(cw, ch);
        }
        window.addEventListener('resize', onResize);

        // --- Close ---
        function close3D() {
            cancelAnimationFrame(animId);
            window.removeEventListener('resize', onResize);
            document.removeEventListener('keydown', onKey3D);
            frameGroup.traverse(function (ch) {
                if (ch.geometry) ch.geometry.dispose();
                if (ch.material) {
                    if (ch.material.map) ch.material.map.dispose();
                    ch.material.dispose();
                }
            });
            if (envMap) envMap.dispose();
            pmremGen.dispose();
            renderer.dispose();
            overlay.remove();
        }
        function onKey3D(e) { if (e.key === 'Escape') close3D(); }
        overlay.querySelector('.viewer3d-close').addEventListener('click', close3D);
        document.addEventListener('keydown', onKey3D);
    }

    // === 3D Wall Viewer (Three.js) ===

    function openWall3DViewer(wallDataUrl, framedSrc, artRelPos) {
        if (typeof THREE === 'undefined') return;

        var overlay = document.createElement('div');
        overlay.className = 'viewer3d';
        overlay.innerHTML =
            '<button class="viewer3d-close" type="button">&times;</button>' +
            '<div class="viewer3d-label">Room Preview</div>' +
            '<div class="viewer3d-canvas-wrap"></div>' +
            '<div class="viewer3d-hint">Orbit to view from different angles &middot; Drag art to reposition</div>' +
            '<div class="viewer3d-controls">' +
                '<div class="viewer3d-glazing">' +
                    '<span class="viewer3d-control-label">Glazing</span>' +
                    '<button class="glazing-opt active" data-glass="museum">Museum Glass</button>' +
                    '<button class="glazing-opt" data-glass="conservation">Conservation Clear</button>' +
                    '<button class="glazing-opt" data-glass="clear">Regular Clear</button>' +
                '</div>' +
            '</div>';

        document.body.appendChild(overlay);

        var canvasWrap = overlay.querySelector('.viewer3d-canvas-wrap');
        var w = canvasWrap.clientWidth;
        var h = canvasWrap.clientHeight;

        var renderer = new THREE.WebGLRenderer({ antialias: true });
        renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        renderer.setSize(w, h);
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.0;
        canvasWrap.appendChild(renderer.domElement);

        var scene = new THREE.Scene();
        scene.background = new THREE.Color(0x181820);

        var camera = new THREE.PerspectiveCamera(50, w / h, 0.1, 100);
        camera.position.set(0, 0, 5);

        var controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.08;
        controls.minDistance = 2;
        controls.maxDistance = 10;
        controls.maxPolarAngle = Math.PI * 0.6;
        controls.minPolarAngle = Math.PI * 0.25;

        scene.add(new THREE.AmbientLight(0xffffff, 0.5));
        var kl = new THREE.DirectionalLight(0xffffff, 0.7);
        kl.position.set(0, 4, 3);
        scene.add(kl);
        var fl = new THREE.DirectionalLight(0xfff5e0, 0.2);
        fl.position.set(-3, 1, 2);
        scene.add(fl);

        var pmremGen = new THREE.PMREMGenerator(renderer);
        var envSc = new THREE.Scene();
        envSc.background = new THREE.Color(0x444444);
        envSc.add(new THREE.DirectionalLight(0xffffff, 0.8));
        var envMap = pmremGen.fromScene(envSc, 0.04).texture;
        scene.environment = envMap;

        var glazingPresets = {
            museum:       { transmission: 0.98, roughness: 0.02, reflectivity: 0.02, ior: 1.5 },
            conservation: { transmission: 0.92, roughness: 0.08, reflectivity: 0.15, ior: 1.5 },
            clear:        { transmission: 0.88, roughness: 0.12, reflectivity: 0.35, ior: 1.5 }
        };
        var glassMat = null;
        var artGroup = new THREE.Group();
        var wallMesh = null;
        var animId = null;

        // Load wall texture
        var texLoader = new THREE.TextureLoader();
        texLoader.load(wallDataUrl, function (wallTex) {
            wallTex.colorSpace = THREE.SRGBColorSpace;
            var wallAspect = wallTex.image.width / wallTex.image.height;
            var wallW = 8, wallH = 8 / wallAspect;

            wallMesh = new THREE.Mesh(
                new THREE.PlaneGeometry(wallW, wallH),
                new THREE.MeshStandardMaterial({ map: wallTex })
            );
            scene.add(wallMesh);

            // Load framed art texture
            texLoader.load(framedSrc, function (artTex) {
                artTex.colorSpace = THREE.SRGBColorSpace;
                var artAspect = artTex.image.width / artTex.image.height;
                var artScale = artRelPos.scale || 0.35;
                var artW = wallW * artScale;
                var artH = artW / artAspect;

                var artMesh = new THREE.Mesh(
                    new THREE.PlaneGeometry(artW, artH),
                    new THREE.MeshStandardMaterial({ map: artTex })
                );

                // Frame around art
                var fb = 0.04;
                var fOW = artW + fb * 2, fOH = artH + fb * 2;
                var fs = new THREE.Shape();
                fs.moveTo(-fOW / 2, -fOH / 2);
                fs.lineTo(fOW / 2, -fOH / 2);
                fs.lineTo(fOW / 2, fOH / 2);
                fs.lineTo(-fOW / 2, fOH / 2);
                fs.closePath();
                var fh = new THREE.Path();
                fh.moveTo(-artW / 2, -artH / 2);
                fh.lineTo(-artW / 2, artH / 2);
                fh.lineTo(artW / 2, artH / 2);
                fh.lineTo(artW / 2, -artH / 2);
                fh.closePath();
                fs.holes.push(fh);
                var frameMesh = new THREE.Mesh(
                    new THREE.ExtrudeGeometry(fs, { depth: 0.04, bevelEnabled: false }),
                    new THREE.MeshStandardMaterial({ color: 0x3d2b1f, roughness: 0.6, metalness: 0.2 })
                );
                frameMesh.position.z = -0.02;

                // Glass
                var gPreset = glazingPresets.museum;
                glassMat = new THREE.MeshPhysicalMaterial({
                    transmission: gPreset.transmission,
                    roughness: gPreset.roughness,
                    reflectivity: gPreset.reflectivity,
                    ior: gPreset.ior,
                    thickness: 0.002,
                    transparent: true
                });
                var glassM = new THREE.Mesh(
                    new THREE.PlaneGeometry(artW, artH),
                    glassMat
                );
                glassM.position.z = 0.022;

                artGroup.add(frameMesh);
                artGroup.add(artMesh);
                artGroup.add(glassM);

                // Position art on wall using relative coords
                var px = (artRelPos.x - 0.5) * wallW;
                var py = -(artRelPos.y - 0.5) * wallH;
                artGroup.position.set(px, py, 0.03);
                scene.add(artGroup);
            });
        });

        // Raycasting drag
        var raycaster = new THREE.Raycaster();
        var mouse = new THREE.Vector2();
        var isDragging = false;
        var dragPlane = new THREE.Plane(new THREE.Vector3(0, 0, 1), 0);
        var dragOffset = new THREE.Vector3();

        renderer.domElement.addEventListener('pointerdown', function (e) {
            mouse.x = (e.offsetX / renderer.domElement.clientWidth) * 2 - 1;
            mouse.y = -(e.offsetY / renderer.domElement.clientHeight) * 2 + 1;
            raycaster.setFromCamera(mouse, camera);
            var hits = raycaster.intersectObject(artGroup, true);
            if (hits.length > 0) {
                isDragging = true;
                controls.enabled = false;
                var pt = new THREE.Vector3();
                raycaster.ray.intersectPlane(dragPlane, pt);
                dragOffset.copy(artGroup.position).sub(pt);
                e.preventDefault();
            }
        });

        renderer.domElement.addEventListener('pointermove', function (e) {
            if (!isDragging) return;
            mouse.x = (e.offsetX / renderer.domElement.clientWidth) * 2 - 1;
            mouse.y = -(e.offsetY / renderer.domElement.clientHeight) * 2 + 1;
            raycaster.setFromCamera(mouse, camera);
            var pt = new THREE.Vector3();
            raycaster.ray.intersectPlane(dragPlane, pt);
            artGroup.position.copy(pt.add(dragOffset));
            artGroup.position.z = 0.03;
        });

        renderer.domElement.addEventListener('pointerup', function () {
            if (isDragging) { isDragging = false; controls.enabled = true; }
        });

        // Glazing controls
        overlay.querySelectorAll('.glazing-opt').forEach(function (btn) {
            btn.addEventListener('click', function () {
                overlay.querySelectorAll('.glazing-opt').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                if (glassMat) {
                    var p = glazingPresets[btn.dataset.glass];
                    glassMat.transmission = p.transmission;
                    glassMat.roughness = p.roughness;
                    glassMat.reflectivity = p.reflectivity;
                    glassMat.needsUpdate = true;
                }
            });
        });

        function animate() {
            animId = requestAnimationFrame(animate);
            controls.update();
            renderer.render(scene, camera);
        }
        animate();

        function onResize() {
            var cw = canvasWrap.clientWidth;
            var ch = canvasWrap.clientHeight;
            camera.aspect = cw / ch;
            camera.updateProjectionMatrix();
            renderer.setSize(cw, ch);
        }
        window.addEventListener('resize', onResize);

        function close3D() {
            cancelAnimationFrame(animId);
            window.removeEventListener('resize', onResize);
            document.removeEventListener('keydown', onKey3D);
            scene.traverse(function (ch) {
                if (ch.geometry) ch.geometry.dispose();
                if (ch.material) {
                    if (ch.material.map) ch.material.map.dispose();
                    ch.material.dispose();
                }
            });
            if (envMap) envMap.dispose();
            pmremGen.dispose();
            renderer.dispose();
            overlay.remove();
        }
        function onKey3D(e) { if (e.key === 'Escape') close3D(); }
        overlay.querySelector('.viewer3d-close').addEventListener('click', close3D);
        document.addEventListener('keydown', onKey3D);
    }

    // === Wall Viewer Modal (Draggable Positioning) ===

    function openWallViewer(framedSrc, styleName) {
        var modal = document.createElement('div');
        modal.className = 'wall-modal';

        modal.innerHTML =
            '<div class="wall-modal-panel wall-modal-wide">' +
                '<div class="wall-modal-header">' +
                    '<div>' +
                        '<div class="wall-modal-title">Preview on Your Wall</div>' +
                        '<div class="wall-modal-subtitle">Upload a photo of your wall, then position <strong>' + esc(styleName) + '</strong> exactly where you want it.</div>' +
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
                '<div class="wall-position-stage hidden">' +
                    '<div class="wall-position-wrap">' +
                        '<img class="wall-bg-image" />' +
                        '<img class="wall-art-overlay" />' +
                    '</div>' +
                    '<div class="wall-position-controls">' +
                        '<div class="wall-size-row">' +
                            '<label>Size</label>' +
                            '<input type="range" class="wall-size-slider" min="10" max="80" value="35" />' +
                        '</div>' +
                        '<div class="wall-position-actions">' +
                            '<button class="btn-secondary wall-retry-btn" type="button">Try Different Photo</button>' +
                            (typeof THREE !== 'undefined'
                                ? '<button class="btn-secondary wall-3d-btn" type="button">' +
                                    '<svg viewBox="0 0 20 20" fill="none" width="14" height="14"><path d="M10 2l8 4v8l-8 4-8-4V6l8-4z" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/><path d="M10 10v8M10 10l8-4M10 10L2 6" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>' +
                                    ' 3D Preview</button>'
                                : '') +
                            '<button class="btn-primary wall-render-btn" type="button">Render Final</button>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="wall-generating hidden">' +
                    '<div class="spinner" style="margin:0 auto 1rem;"><div class="spinner-frame"></div></div>' +
                    '<div>Refining your wall preview<span class="loading-dots"><span>.</span><span>.</span><span>.</span></span></div>' +
                    '<div class="wall-generating-hint">Adding realistic shadows and lighting</div>' +
                '</div>' +
                '<div class="wall-result hidden"></div>' +
                '<div class="wall-error hidden"></div>' +
            '</div>';

        var panel = modal.querySelector('.wall-modal-panel');
        var dropZoneEl = modal.querySelector('#wall-drop-zone');
        var wallFileInput = dropZoneEl.querySelector('input[type="file"]');
        var positionStage = modal.querySelector('.wall-position-stage');
        var positionWrap = modal.querySelector('.wall-position-wrap');
        var wallBgImg = modal.querySelector('.wall-bg-image');
        var artOverlay = modal.querySelector('.wall-art-overlay');
        var sizeSlider = modal.querySelector('.wall-size-slider');
        var generatingEl = modal.querySelector('.wall-generating');
        var resultEl = modal.querySelector('.wall-result');
        var errorEl = modal.querySelector('.wall-error');
        var wallPhotoDataUrl = null;

        // Close modal
        function closeModal() {
            modal.remove();
            document.removeEventListener('keydown', onModalKey);
        }
        function onModalKey(e) { if (e.key === 'Escape') closeModal(); }
        modal.addEventListener('click', function (e) { if (e.target === modal) closeModal(); });
        modal.querySelector('.wall-modal-close').addEventListener('click', closeModal);
        document.addEventListener('keydown', onModalKey);

        // Wall photo upload zone
        dropZoneEl.addEventListener('dragover', function (e) { e.preventDefault(); dropZoneEl.classList.add('drag-over'); });
        dropZoneEl.addEventListener('dragleave', function () { dropZoneEl.classList.remove('drag-over'); });
        dropZoneEl.addEventListener('drop', function (e) {
            e.preventDefault();
            dropZoneEl.classList.remove('drag-over');
            if (e.dataTransfer.files.length > 0) handleWallPhoto(e.dataTransfer.files[0]);
        });
        dropZoneEl.addEventListener('click', function () { wallFileInput.click(); });
        wallFileInput.addEventListener('change', function () {
            if (wallFileInput.files.length > 0) handleWallPhoto(wallFileInput.files[0]);
        });

        function handleWallPhoto(file) {
            if (!file.type.startsWith('image/')) return;
            var reader = new FileReader();
            reader.onload = function (ev) {
                wallPhotoDataUrl = ev.target.result;
                wallBgImg.src = wallPhotoDataUrl;
                artOverlay.src = framedSrc;
                dropZoneEl.classList.add('hidden');
                errorEl.classList.add('hidden');
                resultEl.classList.add('hidden');
                generatingEl.classList.add('hidden');
                positionStage.classList.remove('hidden');
                // Center art at 35% width
                artOverlay.onload = function () {
                    var wrapW = positionWrap.clientWidth;
                    var wrapH = positionWrap.clientHeight;
                    var artW = wrapW * 0.35;
                    artOverlay.style.width = artW + 'px';
                    artOverlay.style.left = ((wrapW - artW) / 2) + 'px';
                    var artH = artOverlay.clientHeight || artW * 0.67;
                    artOverlay.style.top = ((wrapH - artH) / 2) + 'px';
                };
            };
            reader.readAsDataURL(file);
        }

        // Drag art overlay (mouse + touch)
        var isDragging = false, dragOX = 0, dragOY = 0;

        artOverlay.addEventListener('mousedown', startDrag);
        artOverlay.addEventListener('touchstart', startDrag, { passive: false });

        function startDrag(e) {
            isDragging = true;
            var pt = e.touches ? e.touches[0] : e;
            var rect = artOverlay.getBoundingClientRect();
            dragOX = pt.clientX - rect.left;
            dragOY = pt.clientY - rect.top;
            e.preventDefault();
        }

        document.addEventListener('mousemove', moveDrag);
        document.addEventListener('touchmove', moveDrag, { passive: false });
        function moveDrag(e) {
            if (!isDragging) return;
            var pt = e.touches ? e.touches[0] : e;
            var wr = positionWrap.getBoundingClientRect();
            var x = pt.clientX - wr.left - dragOX;
            var y = pt.clientY - wr.top - dragOY;
            x = Math.max(-artOverlay.clientWidth * 0.5, Math.min(x, positionWrap.clientWidth - artOverlay.clientWidth * 0.5));
            y = Math.max(-artOverlay.clientHeight * 0.5, Math.min(y, positionWrap.clientHeight - artOverlay.clientHeight * 0.5));
            artOverlay.style.left = x + 'px';
            artOverlay.style.top = y + 'px';
            e.preventDefault();
        }

        document.addEventListener('mouseup', endDrag);
        document.addEventListener('touchend', endDrag);
        function endDrag() { isDragging = false; }

        // Size slider
        sizeSlider.addEventListener('input', function () {
            var pct = parseInt(this.value) / 100;
            var wrapW = positionWrap.clientWidth;
            artOverlay.style.width = (wrapW * pct) + 'px';
        });

        // Try Different Photo
        modal.querySelector('.wall-retry-btn').addEventListener('click', function () {
            positionStage.classList.add('hidden');
            wallFileInput.value = '';
            wallPhotoDataUrl = null;
            dropZoneEl.classList.remove('hidden');
        });

        // 3D Preview
        var wall3dBtn = modal.querySelector('.wall-3d-btn');
        if (wall3dBtn) {
            wall3dBtn.addEventListener('click', function () {
                var wrapW = positionWrap.clientWidth;
                var wrapH = positionWrap.clientHeight;
                var relPos = {
                    x: (artOverlay.offsetLeft + artOverlay.clientWidth / 2) / wrapW,
                    y: (artOverlay.offsetTop + artOverlay.clientHeight / 2) / wrapH,
                    scale: artOverlay.clientWidth / wrapW
                };
                closeModal();
                openWall3DViewer(wallPhotoDataUrl, framedSrc, relPos);
            });
        }

        // Render Final — canvas composite + AI refinement
        modal.querySelector('.wall-render-btn').addEventListener('click', async function () {
            positionStage.classList.add('hidden');
            generatingEl.classList.remove('hidden');

            try {
                // Composite on canvas
                var canvas = document.createElement('canvas');
                var wallImg = new Image();
                wallImg.src = wallPhotoDataUrl;
                await new Promise(function (res) { wallImg.onload = res; });

                canvas.width = wallImg.naturalWidth;
                canvas.height = wallImg.naturalHeight;
                var ctx = canvas.getContext('2d');
                ctx.drawImage(wallImg, 0, 0);

                // Map art overlay position to canvas coordinates
                var wrapW = positionWrap.clientWidth;
                var wrapH = positionWrap.clientHeight;
                var relX = artOverlay.offsetLeft / wrapW;
                var relY = artOverlay.offsetTop / wrapH;
                var relW = artOverlay.clientWidth / wrapW;

                var artImg = new Image();
                artImg.src = framedSrc;
                await new Promise(function (res) { artImg.onload = res; });

                var drawW = relW * canvas.width;
                var drawH = drawW / (artImg.naturalWidth / artImg.naturalHeight);
                ctx.drawImage(artImg, relX * canvas.width, relY * canvas.height, drawW, drawH);

                // Convert to blob and send
                var blob = await new Promise(function (res) {
                    canvas.toBlob(function (b) { res(b); }, 'image/jpeg', 0.92);
                });

                var formData = new FormData();
                formData.append('compositeImage', blob, 'composite.jpg');

                var response = await fetch('/Home/WallRefine', {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) throw new Error('Server returned ' + response.status);
                var data = await response.json();
                generatingEl.classList.add('hidden');

                if (data.previewImageBase64) {
                    resultEl.innerHTML =
                        '<img src="data:image/png;base64,' + data.previewImageBase64 + '" alt="Your art on the wall" />' +
                        '<div class="wall-result-actions">' +
                            '<button class="btn-secondary wall-result-retry" type="button">Try Another Photo</button>' +
                        '</div>';
                    resultEl.classList.remove('hidden');
                    resultEl.querySelector('.wall-result-retry').addEventListener('click', function () {
                        resultEl.classList.add('hidden');
                        wallFileInput.value = '';
                        wallPhotoDataUrl = null;
                        dropZoneEl.classList.remove('hidden');
                    });
                } else {
                    throw new Error('No preview image returned');
                }
            } catch (err) {
                console.error('Wall refine failed:', err);
                generatingEl.classList.add('hidden');
                errorEl.innerHTML =
                    '<p>Could not generate the wall preview. Please try again.</p>' +
                    '<button class="btn-secondary wall-error-retry" type="button">Try Again</button>';
                errorEl.classList.remove('hidden');
                errorEl.querySelector('.wall-error-retry').addEventListener('click', function () {
                    errorEl.classList.add('hidden');
                    positionStage.classList.remove('hidden');
                });
            }
        });

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

    // =========================================================================
    // ART PRINT BROWSE & DISCOVERY
    // =========================================================================

    // Expose selectMode globally for onclick in HTML
    window.selectMode = function(mode) {
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        var btn = document.getElementById('mode-' + mode);
        if (btn) btn.classList.add('active');

        if (mode === 'upload') {
            showSection(uploadSection);
        } else if (mode === 'browse') {
            showSection(browseSection);
            if (!document.getElementById('browse-prints-grid').children.length) {
                loadBrowseFilters();
                loadArtPrints(true);
            }
        } else if (mode === 'discover') {
            showSection(discoverSection);
            initDiscoveryColorPalette();
            restartDiscovery();
        }
    };

    // === Browse: Load Filters ===
    var browseFiltersLoaded = false;
    function loadBrowseFilters() {
        if (browseFiltersLoaded) return;
        browseFiltersLoaded = true;
        fetch('/Home/ArtPrintFilters')
            .then(r => r.json())
            .then(data => {
                populateFilterSelect('filter-vendor', data.vendors);
                populateFilterSelect('filter-artist', data.artists);
                populateFilterSelect('filter-genre', data.genres);
                populateFilterSelect('filter-style', data.styles);
                populateFilterSelect('filter-mood', data.moods);
            })
            .catch(() => {});
    }

    function populateFilterSelect(id, options) {
        var sel = document.getElementById(id);
        if (!sel || !options) return;
        var first = sel.options[0];
        sel.innerHTML = '';
        sel.appendChild(first);
        options.forEach(opt => {
            var o = document.createElement('option');
            o.value = opt;
            o.textContent = opt;
            sel.appendChild(o);
        });
    }

    // === Browse: Toggle Filters ===
    window.toggleBrowseFilters = function() {
        var panel = document.getElementById('browse-filter-panel');
        panel.classList.toggle('hidden');
    };

    // === Browse: Load Art Prints ===
    window.loadArtPrints = function(reset) {
        if (artPrintLoading) return;
        if (reset) {
            artPrintPage = 1;
            artPrintHasMore = true;
            document.getElementById('browse-prints-grid').innerHTML = '';
        }
        if (!artPrintHasMore) return;

        artPrintLoading = true;
        var loadEl = document.getElementById('browse-loading');
        if (loadEl) loadEl.classList.remove('hidden');

        var params = new URLSearchParams();
        var q = document.getElementById('browse-query')?.value;
        var vendor = document.getElementById('filter-vendor')?.value;
        var artist = document.getElementById('filter-artist')?.value;
        var genre = document.getElementById('filter-genre')?.value;
        var style = document.getElementById('filter-style')?.value;
        var mood = document.getElementById('filter-mood')?.value;
        var orientation = document.getElementById('filter-orientation')?.value;

        if (q) params.set('query', q);
        if (vendor) params.set('vendor', vendor);
        if (artist) params.set('artist', artist);
        if (genre) params.set('genre', genre);
        if (style) params.set('style', style);
        if (mood) params.set('mood', mood);
        if (orientation) params.set('orientation', orientation);
        params.set('page', artPrintPage);
        params.set('pageSize', 24);

        fetch('/Home/BrowseArtPrints?' + params)
            .then(r => r.json())
            .then(data => {
                renderPrintCards(data.prints, false);
                document.getElementById('browse-info').textContent =
                    data.totalCount + ' prints found' + (data.totalPages > 1 ? ' (page ' + data.page + '/' + data.totalPages + ')' : '');
                artPrintPage = data.page + 1;
                artPrintHasMore = data.page < data.totalPages;
                updateActiveFilterChips();
            })
            .catch(e => {
                document.getElementById('browse-info').textContent = 'Error loading prints';
            })
            .finally(() => {
                artPrintLoading = false;
                if (loadEl) loadEl.classList.add('hidden');
            });
    };

    // === Browse: Render Print Cards ===
    function renderPrintCards(prints, clear, containerId) {
        var grid = document.getElementById(containerId || 'browse-prints-grid');
        if (clear) grid.innerHTML = '';

        prints.forEach(p => {
            var card = document.createElement('div');
            card.className = 'print-card';
            card.onclick = function() { showPrintDetail(p); };

            var imgSrc = p.imageUrl || p.thumbnailUrl || '';
            card.innerHTML =
                (imgSrc ? '<img class="print-card-image" src="' + esc(imgSrc) + '" alt="' + esc(p.title) + '" loading="lazy" onerror="this.style.display=\'none\'" />'
                    : '<div class="print-card-image" style="display:flex;align-items:center;justify-content:center;color:var(--text-secondary,#8888a0);font-size:0.75rem;">No Image</div>') +
                '<div class="print-card-overlay">' +
                    '<div class="print-card-title">' + esc(p.title) + '</div>' +
                    '<div class="print-card-artist">' + esc(p.artist || 'Unknown Artist') + '</div>' +
                '</div>';

            grid.appendChild(card);
        });
    }

    // === Browse: Active Filter Chips ===
    function updateActiveFilterChips() {
        var container = document.getElementById('browse-active-filters');
        if (!container) return;
        container.innerHTML = '';

        var filters = [
            { id: 'filter-vendor', label: 'Vendor' },
            { id: 'filter-artist', label: 'Artist' },
            { id: 'filter-genre', label: 'Genre' },
            { id: 'filter-style', label: 'Style' },
            { id: 'filter-mood', label: 'Mood' },
            { id: 'filter-orientation', label: 'Orientation' }
        ];

        filters.forEach(f => {
            var val = document.getElementById(f.id)?.value;
            if (val) {
                var chip = document.createElement('span');
                chip.className = 'filter-chip';
                chip.innerHTML = esc(f.label) + ': ' + esc(val) +
                    ' <span class="filter-chip-remove" data-filter="' + f.id + '">&times;</span>';
                chip.querySelector('.filter-chip-remove').onclick = function() {
                    document.getElementById(f.id).value = '';
                    loadArtPrints(true);
                };
                container.appendChild(chip);
            }
        });
    }

    // === Browse: Infinite Scroll ===
    window.addEventListener('scroll', function() {
        if (!browseSection || browseSection.classList.contains('hidden')) return;
        if (artPrintLoading || !artPrintHasMore) return;
        if ((window.innerHeight + window.scrollY) >= document.body.offsetHeight - 300) {
            loadArtPrints(false);
        }
    });

    // === Browse: Search on Enter ===
    var browseQuery = document.getElementById('browse-query');
    if (browseQuery) {
        browseQuery.addEventListener('keydown', function(e) {
            if (e.key === 'Enter') loadArtPrints(true);
        });
    }

    // === Browse: Filter change handlers ===
    ['filter-vendor', 'filter-artist', 'filter-genre', 'filter-style', 'filter-mood', 'filter-orientation'].forEach(id => {
        var el = document.getElementById(id);
        if (el) el.addEventListener('change', function() { loadArtPrints(true); });
    });

    // =========================================================================
    // DISCOVERY WIZARD
    // =========================================================================

    var discoverySteps = ['room', 'mood', 'colors', 'style', 'results'];

    function initDiscoveryColorPalette() {
        var palette = document.getElementById('discover-color-palette');
        if (!palette || palette.children.length > 0) return;

        var colors = [
            '#E74C3C', '#E67E22', '#F1C40F', '#2ECC71', '#1ABC9C',
            '#3498DB', '#2C3E50', '#9B59B6', '#E91E63', '#795548',
            '#FF9800', '#CDDC39', '#00BCD4', '#607D8B', '#F5F5DC',
            '#FAEBD7', '#D4A574', '#8B4513', '#000000', '#FFFFFF'
        ];

        colors.forEach(hex => {
            var swatch = document.createElement('button');
            swatch.className = 'discover-color-swatch';
            swatch.style.backgroundColor = hex;
            swatch.dataset.color = hex;
            swatch.onclick = function() {
                if (swatch.classList.contains('selected')) {
                    swatch.classList.remove('selected');
                    discoverySelections.colors = discoverySelections.colors.filter(c => c !== hex);
                } else if (discoverySelections.colors.length < 3) {
                    swatch.classList.add('selected');
                    discoverySelections.colors.push(hex);
                }
                var nextBtn = document.getElementById('colors-next-btn');
                if (nextBtn) nextBtn.style.display = discoverySelections.colors.length > 0 ? '' : 'none';
            };
            palette.appendChild(swatch);
        });
    }

    window.selectDiscoveryOption = function(step, btn) {
        var value = btn.dataset.value;

        // Deselect siblings
        btn.parentElement.querySelectorAll('.discover-option').forEach(b => b.classList.remove('selected'));
        btn.classList.add('selected');

        discoverySelections[step] = value;

        // Auto-advance after a brief delay
        setTimeout(function() { advanceDiscoveryStep(step); }, 300);
    };

    window.advanceDiscoveryStep = function(currentStep) {
        var idx = discoverySteps.indexOf(currentStep);
        if (idx < 0 || idx >= discoverySteps.length - 1) return;

        var nextStep = discoverySteps[idx + 1];

        document.getElementById('discover-step-' + currentStep)?.classList.remove('active');
        document.getElementById('discover-step-' + nextStep)?.classList.add('active');

        if (nextStep === 'results') {
            loadDiscoveryResults();
        }
    };

    window.loadDiscoveryResults = function() {
        var grid = document.getElementById('discover-prints-grid');
        var loadEl = document.getElementById('discover-loading');
        if (loadEl) loadEl.classList.remove('hidden');

        fetch('/Home/DiscoverPrints', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                room: discoverySelections.room,
                mood: discoverySelections.mood,
                colors: discoverySelections.colors,
                style: discoverySelections.style,
                excludeIds: discoveryExcludeIds,
                limit: 24
            })
        })
        .then(r => r.json())
        .then(data => {
            renderPrintCards(data.prints, false, 'discover-prints-grid');
        })
        .catch(() => {})
        .finally(() => {
            if (loadEl) loadEl.classList.add('hidden');
        });
    };

    window.restartDiscovery = function() {
        discoverySelections = { room: null, mood: null, colors: [], style: null };
        discoveryExcludeIds = [];

        discoverySteps.forEach(step => {
            var el = document.getElementById('discover-step-' + step);
            if (el) el.classList.remove('active');
        });
        document.getElementById('discover-step-room')?.classList.add('active');

        // Reset selected states
        document.querySelectorAll('.discover-option.selected').forEach(b => b.classList.remove('selected'));
        document.querySelectorAll('.discover-color-swatch.selected').forEach(s => s.classList.remove('selected'));
        document.getElementById('discover-prints-grid').innerHTML = '';
        var nextBtn = document.getElementById('colors-next-btn');
        if (nextBtn) nextBtn.style.display = 'none';
    };

    // =========================================================================
    // PRINT DETAIL MODAL
    // =========================================================================

    window.showPrintDetail = function(print) {
        currentPrintDetail = print;
        var modal = document.getElementById('print-detail-modal');

        document.getElementById('print-detail-image').src = print.imageUrl || print.thumbnailUrl || '';
        document.getElementById('print-detail-title').textContent = print.title || 'Untitled';
        document.getElementById('print-detail-artist').textContent = (print.artist || 'Unknown Artist') + '  \u2022  ' + (print.vendorName || '');

        // Badges
        var badges = [];
        if (print.genre) badges.push(print.genre);
        if (print.aiStyle || print.style) badges.push(print.aiStyle || print.style);
        if (print.aiMood) badges.push(print.aiMood);
        if (print.orientation) badges.push(print.orientation);
        if (print.imageWidthIn && print.imageHeightIn)
            badges.push(print.imageWidthIn + '" \u00d7 ' + print.imageHeightIn + '"');

        document.getElementById('print-detail-badges').innerHTML =
            badges.map(b => '<span class="print-detail-badge">' + esc(b) + '</span>').join('');

        document.getElementById('print-detail-desc').textContent = print.aiDescription || '';

        // Colors
        var colorHtml = '';
        [print.primaryColorHex, print.secondaryColorHex, print.tertiaryColorHex].filter(Boolean).forEach(hex => {
            colorHtml += '<span class="print-detail-color" style="background:' + hex + ';" title="' + hex + '"></span>';
        });
        document.getElementById('print-detail-colors').innerHTML = colorHtml;

        modal.classList.remove('hidden');
    };

    window.closePrintDetail = function() {
        document.getElementById('print-detail-modal').classList.add('hidden');
        currentPrintDetail = null;
    };

    // Close modal on Escape
    document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape') closePrintDetail();
    });

    // === Frame This Print ===
    window.frameThisPrint = function() {
        if (!currentPrintDetail || !currentPrintDetail.imageUrl) return;
        closePrintDetail();

        // Switch to upload mode, show loading
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        document.getElementById('mode-upload')?.classList.add('active');
        showSection(loadingSection);

        // Download the image and run through analysis pipeline
        fetch('/Home/AnalyzePrint', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ imageUrl: currentPrintDetail.imageUrl })
        })
        .then(r => r.json())
        .then(analysis => {
            if (analysis.error) {
                errorMessage.textContent = analysis.error;
                showSection(errorSection);
                return;
            }
            currentAnalysis = analysis;

            // Now fetch the image as a blob for FrameOne calls
            return fetch(currentPrintDetail.imageUrl)
                .then(r => r.blob())
                .then(blob => {
                    currentFile = new File([blob], 'art-print.jpg', { type: blob.type || 'image/jpeg' });
                    previewImage.src = URL.createObjectURL(currentFile);
                    generateFramesSequentially(currentFile, analysis);
                });
        })
        .catch(e => {
            errorMessage.textContent = 'Failed to analyze the art print.';
            showSection(errorSection);
        });
    };

    // === More Like This ===
    window.moreLikeThis = function() {
        if (!currentPrintDetail) return;
        var printId = currentPrintDetail.id;
        closePrintDetail();

        // Switch to browse mode and show similar prints
        document.querySelectorAll('.mode-btn').forEach(b => b.classList.remove('active'));
        document.getElementById('mode-browse')?.classList.add('active');
        showSection(browseSection);

        document.getElementById('browse-prints-grid').innerHTML = '';
        document.getElementById('browse-info').textContent = 'Finding similar prints...';
        var loadEl = document.getElementById('browse-loading');
        if (loadEl) loadEl.classList.remove('hidden');

        fetch('/Home/SimilarPrints', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ printId: printId, excludeIds: discoveryExcludeIds, limit: 24 })
        })
        .then(r => r.json())
        .then(data => {
            renderPrintCards(data.prints, true);
            document.getElementById('browse-info').textContent = data.prints.length + ' similar prints found';
        })
        .catch(() => {
            document.getElementById('browse-info').textContent = 'Error finding similar prints';
        })
        .finally(() => {
            if (loadEl) loadEl.classList.add('hidden');
        });
    };

    // === Not This ===
    window.notLikeThis = function() {
        if (!currentPrintDetail) return;
        discoveryExcludeIds.push(currentPrintDetail.id);
        closePrintDetail();
    };

})();
