/**
 * NanoBanana Gemini Automation Scripts
 * 
 * CDP를 통해 Gemini 웹페이지에 주입되어 이미지 처리 자동화를 수행합니다.
 * EdgeCdpAutomation.cs에서 EvaluateFunctionAsync로 호출됩니다.
 * 
 * 사용법: 각 함수를 CDP를 통해 개별적으로 호출
 */

const NanoBanana = {
    // ========== 유틸리티 (Antigravity 에이전틱 자동화 규격) ==========

    /**
     * Shadow DOM 내부까지 탐색하는 단일 요소 선택자
     */
    queryShadowSelector: function (selector, root = document) {
        const parts = selector.split('>>>');
        let current = root;
        for (let i = 0; i < parts.length; i++) {
            const part = parts[i].trim();
            if (i > 0) current = current.shadowRoot || current;
            current = current.querySelector(part);
            if (!current) break;
        }
        return current;
    },

    /**
     * Shadow DOM 내부까지 탐색하는 다중 요소 선택자
     */
    queryShadowSelectorAll: function (selector, root = document) {
        const parts = selector.split('>>>');
        let currentRoots = [root];
        let lastElements = [];

        for (let i = 0; i < parts.length; i++) {
            const part = parts[i].trim();
            const nextRoots = [];
            lastElements = [];

            for (const r of currentRoots) {
                const target = i > 0 ? (r.shadowRoot || r) : r;
                const found = target.querySelectorAll(part);
                for (const el of found) {
                    lastElements.push(el);
                    nextRoots.push(el);
                }
            }
            currentRoots = nextRoots;
            if (currentRoots.length === 0) break;
        }
        return lastElements;
    },

    /**
     * 요소가 가시적이고 상호작용 가능한지 확인
     */
    isInteractable: function (el) {
        if (!el) return false;
        const style = window.getComputedStyle(el);
        return el.offsetParent !== null &&
            style.display !== 'none' &&
            style.visibility !== 'hidden' &&
            style.opacity !== '0' &&
            !el.disabled;
    },

    /**
     * 요소가 나타날 때까지 대기 (Shadow DOM 지원)
     */
    waitForElement: async function (selector, timeout = 15000) {
        const startTime = Date.now();
        console.log(`[NanoBanana] Waiting for: ${selector}`);
        while (Date.now() - startTime < timeout) {
            const el = selector.includes('>>>') ? this.queryShadowSelector(selector) : document.querySelector(selector);
            if (el && this.isInteractable(el)) return el;
            await new Promise(r => setTimeout(r, 300));
        }
        console.warn(`[NanoBanana] Timeout waiting for: ${selector}`);
        return null;
    },

    /**
     * 짧은 딜레이
     */
    delay: function (ms) {
        return new Promise(r => setTimeout(r, ms));
    },

    /**
     * 요소 클릭 (안전하게, 마우스 이벤트 포함)
     */
    safeClick: function (element) {
        if (!element) return false;
        try {
            console.log(`[NanoBanana] Clicking element:`, element.tagName, element.className);
            element.scrollIntoView({ behavior: 'instant', block: 'center' });

            // 일반 클릭 시도
            element.click();

            // 보조 이벤트 발생
            const events = ['mousedown', 'mouseup', 'pointerdown', 'pointerup'];
            events.forEach(evt => {
                element.dispatchEvent(new MouseEvent(evt, { bubbles: true, cancelable: true, view: window }));
            });

            return true;
        } catch (e) {
            console.error('[NanoBanana] Click failed:', e);
            return false;
        }
    },

    /**
     * 면책 조항 또는 동의 팝업 처리
     */
    handleDisclaimer: async function () {
        const disclaimerButtons = Array.from(document.querySelectorAll('button')).filter(btn => {
            const txt = btn.innerText.toLowerCase();
            return (txt.includes('동의') || txt.includes('수락') || txt.includes('agree') || txt.includes('accept')) &&
                this.isInteractable(btn);
        });

        if (disclaimerButtons.length > 0) {
            console.log('[NanoBanana] Disclaimer/Consent detected, clicking...');
            this.safeClick(disclaimerButtons[0]);
            await this.delay(1000);
            return true;
        }
        return false;
    },

    // ========== 모드 및 환경 설정 ==========

    /**
     * Pro 모드 활성화 (강력한 선택자 적용)
     */
    selectProMode: async function () {
        try {
            console.log('[NanoBanana] Attempting to select Pro mode...');

            // 전역 팝업 처리
            await this.handleDisclaimer();

            // 1. 현재 모드 확인 (이미 Pro인지 체크)
            const currentModeText = document.querySelector('.input-area-switch text, .input-area-switch .mat-mdc-button-touch-target')?.parentElement?.innerText || '';
            if (currentModeText.toLowerCase().includes('pro')) {
                console.log('[NanoBanana] Already in Pro mode.');
                return { success: true, message: '이미 Pro 모드입니다' };
            }

            // 2. 모드 메뉴 열기
            const modeBtn = await this.waitForElement('button.input-area-switch, button[aria-label*="모드"]');
            if (!modeBtn) return { success: false, message: '모드 선택 버튼을 찾을 수 없습니다' };

            this.safeClick(modeBtn);
            await this.delay(500); // Python 타이밍 참조: 500ms

            // 3. Pro 옵션 선택 (Shadow DOM 및 다중 선택자)
            const menuItems = Array.from(document.querySelectorAll('button[role="menuitemradio"], button.mat-mdc-menu-item, .mat-mdc-menu-content button, button.bard-mode-list-button'));
            const proItem = menuItems.find(item => item.innerText.includes('Pro') || item.innerText.includes('프로'));

            if (proItem) {
                this.safeClick(proItem);
                await this.delay(500); // Python 타이밍 참조: 500ms
                console.log('[NanoBanana] Pro mode selected.');
                return { success: true, message: 'Pro 모드 활성화됨' };
            }

            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            return { success: false, message: 'Pro 옵션을 찾을 수 없습니다' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    /**
     * 이미지 생성 모드 활성화 (고도화된 선택자)
     */
    enableImageGeneration: async function () {
        try {
            console.log('[NanoBanana] Enabling image generation tool...');

            // 전역 팝업 처리
            await this.handleDisclaimer();

            // 1. 도구 버튼 찾기
            const toolsBtn = await this.waitForElement('button.toolbox-drawer-button, button[aria-label*="도구"], button[aria-label*="Tools"]');
            if (!toolsBtn) return { success: false, message: '도구 버튼을 찾을 수 없습니다' };

            this.safeClick(toolsBtn);
            await this.delay(1000);

            // 2. 이미지 생성하기 옵션 (Aria-label 및 텍스트 조합)
            const allItems = Array.from(document.querySelectorAll('button, .mat-mdc-list-item, [role="menuitem"]'));
            const targetItem = allItems.find(item =>
                item.innerText.includes('이미지 생성') ||
                item.innerText.includes('Create image') ||
                item.getAttribute('aria-label')?.includes('이미지 생성')
            );

            if (targetItem) {
                this.safeClick(targetItem);
                await this.delay(800);
                document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                return { success: true, message: '이미지 생성 모드 활성화됨' };
            }

            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
            // 실패해도 Pro 모델 등에서는 기본 활성화되어 있을 수 있으므로 성공으로 간주하고 진행
            console.warn('[NanoBanana] 이미지 생성 옵션을 찾지 못했으나, 기본 활성화를 가정하고 진행합니다.');
            return { success: true, message: '이미지 생성 옵션 없음 (기본 활성 가정)' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    // ========== 이미지 업로드 ==========

    /**
     * 파일 업로드 메뉴 열기
     */
    openUploadMenu: async function () {
        try {
            console.log('[NanoBanana] Opening upload menu...');
            const uploadSelectors = [
                'button[aria-label*="업로드"]',
                'button[aria-label*="upload"]',
                'button.upload-card-button'
            ];

            let menuBtn = null;
            for (const sel of uploadSelectors) {
                menuBtn = document.querySelector(sel);
                if (menuBtn && this.isInteractable(menuBtn)) break;
            }

            if (!menuBtn) return { success: false, message: '업로드 메뉴 버튼을 찾을 수 없습니다' };

            // 0. 면책 조항(버튼: 동의, Agree 등)이 떠있으면 클릭
            await this.handleDisclaimer();

            this.safeClick(menuBtn);
            await this.delay(1000);

            // 클릭 후에도 면책 조항이 뜨면 한 번 더 체크
            await this.handleDisclaimer();

            // 파일 업로드 서브메뉴
            const subItems = Array.from(document.querySelectorAll('button, [role="menuitem"]'));
            const fileBtn = subItems.find(item =>
                item.innerText.includes('파일 업로드') ||
                item.innerText.includes('Upload file') ||
                item.getAttribute('aria-label')?.includes('파일 업로드')
            );

            if (fileBtn) {
                this.safeClick(fileBtn);
                await this.delay(500);
                return { success: true, message: '파일 업로드 다이얼로그 연동됨' };
            }

            return { success: true, message: '업로드 메뉴 오픈됨' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    /**
     * 파일 input 요소 찾기 (send_keys 용)
     * @returns {HTMLInputElement|null}
     */
    getFileInput: function () {
        const inputs = document.querySelectorAll('input[type="file"]');
        return inputs.length > 0 ? inputs[inputs.length - 1] : null;
    },

    /**
     * 이미지 업로드 완료 대기 (강화된 선택자)
     * @param {number} timeout - 타임아웃 (ms)
     * @returns {Promise<boolean>}
     */
    waitForImageUpload: async function (timeout = 60000) {
        const startTime = Date.now();
        console.log('[NanoBanana] Waiting for image upload confirmation...');

        while (Date.now() - startTime < timeout) {
            // 다양한 선택자로 업로드된 이미지 확인
            const selectors = [
                // 입력창 영역의 업로드된 첨부 파일
                '.input-area-container img',
                '.rich-textarea img',
                '.ql-editor img',
                // 파일 첨부 영역
                '.file-chip',
                '.attachment-chip',
                'content-container .attachment-thumbnail',
                // Blob URL 이미지
                "img[src^='blob:']",
                // 파일 이름 표시 칩
                '[data-filename]',
                '.uploaded-file-name',
                // 삭제 버튼이 있는 첨부 영역 (X 버튼)
                'button[aria-label*="삭제"], button[aria-label*="Remove"], button[aria-label*="Delete"]'
            ];

            for (const sel of selectors) {
                const elements = document.querySelectorAll(sel);
                if (elements.length > 0) {
                    console.log(`[NanoBanana] Upload confirmed via selector: ${sel}`);
                    return true;
                }
            }

            await this.delay(300);
        }
        console.warn('[NanoBanana] Upload confirmation timeout');
        return false;
    },

    // ========== 입력 및 전송 제어 ==========

    /**
     * 입력창에 프롬프트 작성 (React 상태 동기화 포함)
     */
    writePrompt: async function (text) {
        try {
            console.log(`[NanoBanana] Writing prompt: "${text.substring(0, 30)}..."`);
            const input = await this.waitForElement('.ql-editor, [contenteditable="true"]');

            if (!input) return { success: false, message: '입력창을 찾을 수 없습니다' };

            input.focus();

            // 1. execCommand 로 물리적 타이핑 시뮬레이션
            document.execCommand('selectAll', false, null);
            document.execCommand('delete', false, null);
            await this.delay(100);
            document.execCommand('insertText', false, text);

            // 2. React/Angular 상태 업데이트 유도 (이벤트 강제 발생)
            const events = ['input', 'change', 'blur'];
            events.forEach(name => {
                input.dispatchEvent(new Event(name, { bubbles: true }));
            });

            await this.delay(300);
            return { success: true, message: '프롬프트 주입 완료' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    /**
     * 전송 버튼 클릭 (상태 감지 및 중복 클릭 방지)
     */
    sendMessage: async function (timeout = 30000) {
        try {
            console.log('[NanoBanana] Sending message...');
            const startTime = Date.now();

            while (Date.now() - startTime < timeout) {
                // 전송 버튼 선택자 (Aria-label 기반이 가장 정확)
                const sendBtn = document.querySelector('button.send-button, button[aria-label*="보내기"], button[aria-label*="Send"]');

                if (sendBtn && this.isInteractable(sendBtn)) {
                    // 비활성화 여부 재확인 (React 상태 대기)
                    if (sendBtn.getAttribute('aria-disabled') !== 'true') {
                        this.safeClick(sendBtn);
                        return { success: true, message: '메시지 전송 성공' };
                    }
                }
                await this.delay(400);
            }

            // Fallback: Enter 키
            const editor = document.querySelector('.ql-editor');
            if (editor) {
                editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true, keyCode: 13 }));
                return { success: true, message: 'Enter 키 전송 시도' };
            }

            return { success: false, message: '전송 버튼 활성화 실패' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    // ========== 응답 대기 및 이미지 다운로드 ==========

    /**
     * 응답 생성 완료 대기 (GeminiScripts.cs 규격과 동기화)
     */
    waitForResponse: async function (timeout = 180000) {
        console.log('[NanoBanana] Waiting for AI response...');
        const startTime = Date.now();
        let lastResponseText = '';
        let stableCount = 0;

        while (Date.now() - startTime < timeout) {
            // 1. 생성 중 여부 판단 (여러 지표 확인)
            const isBusy = (function () {
                const sendBtn = document.querySelector('.send-button');
                if (sendBtn && (sendBtn.classList.contains('stop') || sendBtn.querySelector('mat-icon')?.textContent === 'stop')) return true;

                const lastMarkdown = Array.from(document.querySelectorAll('.markdown')).pop();
                if (lastMarkdown && (lastMarkdown.getAttribute('aria-busy') === 'true' || lastMarkdown.classList.contains('generating'))) return true;

                const stopBtn = document.querySelector('button[aria-label*="중지"], button[aria-label*="Stop"]');
                if (stopBtn && stopBtn.offsetParent !== null) return true;

                return false;
            })();

            if (isBusy) {
                stableCount = 0;
                await this.delay(1500);
                continue;
            }

            // 2. 응답 내용 추출 및 안정성 확인
            const responseElements = document.querySelectorAll('.model-response-text, .markdown:not(.user-prompt)');
            const currentResponse = responseElements.length > 0 ? responseElements[responseElements.length - 1].innerText : '';

            if (currentResponse && currentResponse === lastResponseText) {
                stableCount++;
                // 3회 연속(약 4.5초) 변화 없으면 완료
                if (stableCount >= 3) {
                    const hasImage = !!document.querySelector("img[src*='googleusercontent'], .generated-image, model-response img");
                    return { success: true, hasImage, message: '응답 생성 완료' };
                }
            } else {
                stableCount = 0;
                lastResponseText = currentResponse;
            }

            await this.delay(1500);
        }

        return { success: false, message: '응답 대기 시간 초과' };
    },

    /**
     * 생성된 이미지 원본 크기 다운로드 (강화된 호버 및 감지)
     */
    downloadOriginalImage: async function () {
        try {
            console.log('[NanoBanana] Searching for generated image to download...');

            // 1. 이미지 찾기 (Shadow DOM 포함)
            const imgSelectors = [
                'img[src*="googleusercontent"]',
                '.model-response img',
                'button.image-button img',
                '.response-container img',
                "img[src*='blob:']"
            ];

            let targetImg = null;
            for (const sel of imgSelectors) {
                const imgs = document.querySelectorAll(sel);
                if (imgs.length > 0) {
                    targetImg = imgs[imgs.length - 1]; // 가장 최신 이미지
                    if (this.isInteractable(targetImg)) break;
                }
            }

            if (!targetImg) return { success: false, message: '이미지를 찾을 수 없습니다' };

            // 2. 다운로드 버튼 표시를 위한 정밀 호버 및 상호작용
            console.log('[NanoBanana] Hovering over image to reveal download button...');
            targetImg.scrollIntoView({ behavior: 'instant', block: 'center' });

            // 호버 이벤트 시뮬레이션
            const rect = targetImg.getBoundingClientRect();
            const hoverEvt = new MouseEvent('mouseenter', {
                bubbles: true,
                cancelable: true,
                clientX: rect.left + rect.width / 2,
                clientY: rect.top + rect.height / 2
            });
            targetImg.dispatchEvent(hoverEvt);
            await this.delay(1000);

            // 3. 다운로드 버튼 선택 (다중 선택자)
            const downloadBtn = await this.waitForElement('button[aria-label*="다운로드"], button[aria-label*="Download"], button.generated-image-button, .on-hover-button button');
            if (downloadBtn) {
                console.log('[NanoBanana] Download button found, clicking...');
                this.safeClick(downloadBtn);
                return { success: true, message: '다운로드 시작됨' };
            }

            // 버튼이 안 보이면 직접 부모 레이어에서 찾기
            const parentContainer = targetImg.closest('.model-response, .chat-history, .response-container');
            if (parentContainer) {
                const fallbackBtn = parentContainer.querySelector('button[aria-label*="다운로드"]');
                if (fallbackBtn) {
                    this.safeClick(fallbackBtn);
                    return { success: true, message: '다운로드 시작됨 (폴백)' };
                }
            }

            return { success: false, message: '다운로드 버튼을 찾을 수 없습니다' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    // ========== 채팅 관리 ==========

    /**
     * 현재 채팅 삭제
     */
    deleteCurrentChat: async function () {
        try {
            console.log('[NanoBanana] Deleting current chat...');
            const menuBtn = await this.waitForElement('button[aria-label*="대화 작업"], button[aria-label*="actions"]');
            if (!menuBtn) return { success: false, message: '메뉴 버튼 없음' };

            this.safeClick(menuBtn);
            await this.delay(600);

            const deleteItem = Array.from(document.querySelectorAll('[role="menuitem"], button.mat-mdc-menu-item'))
                .find(el => el.innerText.includes('삭제') || el.innerText.includes('Delete'));

            if (!deleteItem) {
                document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                return { success: false, message: '삭제 항목 없음' };
            }

            this.safeClick(deleteItem);
            await this.delay(800);

            const confirmBtn = Array.from(document.querySelectorAll('mat-dialog-actions button, .mat-mdc-dialog-actions button'))
                .find(el => el.innerText.includes('삭제') || el.innerText.includes('Delete'));

            if (confirmBtn) {
                this.safeClick(confirmBtn);
                await this.delay(1000);
                return { success: true, message: '삭제 완료' };
            }

            return { success: false, message: '확인 버튼 없음' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    // ========== 새 채팅 시작 ==========

    /**
     * 새 채팅 시작 (메인 페이지로 이동)
     * @returns {Promise<{success: boolean, message: string}>}
     */
    startNewChat: async function () {
        try {
            window.location.href = 'https://gemini.google.com/app';
            return { success: true, message: '새 채팅 페이지로 이동 중...' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    // ========== CDP 강화 기능 (자동 이미지 업로드) ==========

    // ========== 파일 전송 고도화 (Base64 직접 주입) ==========

    /**
     * Base64 이미지를 File 객체로 변환하여 input에 주입 (개선된 버전)
     */
    uploadImageFromPath: async function (base64Data, filename) {
        try {
            console.log(`[NanoBanana] Injecting image file: ${filename}`);

            // 1. Base64 → File 변환
            let mimeType = 'image/png';
            let pureBase64 = base64Data;
            if (base64Data.startsWith('data:')) {
                const match = base64Data.match(/^data:([^;]+);base64,(.+)$/);
                if (match) { mimeType = match[1]; pureBase64 = match[2]; }
            }

            const bin = atob(pureBase64);
            const buf = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
            const file = new File([buf], filename, { type: mimeType });
            console.log(`[NanoBanana] File created: ${file.name}, size: ${file.size} bytes`);

            // 2. 업로드 메뉴를 열어 input[type=file] 생성 유도
            let input = document.querySelector('input[type="file"]');
            if (!input) {
                console.log('[NanoBanana] File input not found, opening upload menu...');

                // 업로드 버튼 클릭
                const uploadBtn = document.querySelector('button[aria-label*="업로드"], button[aria-label*="upload"], button.upload-card-button');
                if (uploadBtn) {
                    this.safeClick(uploadBtn);
                    await this.delay(800);

                    // 파일 업로드 서브메뉴 클릭
                    const subItems = Array.from(document.querySelectorAll('button, [role="menuitem"]'));
                    const fileBtn = subItems.find(item =>
                        item.innerText.includes('파일 업로드') ||
                        item.innerText.includes('Upload file') ||
                        item.getAttribute('aria-label')?.includes('파일 업로드')
                    );
                    if (fileBtn) {
                        this.safeClick(fileBtn);
                        await this.delay(800);
                    }
                }

                // 다시 input 확인 (숨겨진 요소 포함 모든 input 검색 후 마지막 요소 선택)
                const allInputs = document.querySelectorAll('input[type="file"]');
                if (allInputs.length > 0) {
                    input = allInputs[allInputs.length - 1];
                    console.log(`[NanoBanana] Found ${allInputs.length} file inputs, using the last one.`);
                }
            }

            // 여전히 없으면 body 전체에서 검색 (최후의 수단)
            if (!input) {
                const deepInputs = document.querySelectorAll('input[type="file"]');
                if (deepInputs.length > 0) input = deepInputs[deepInputs.length - 1];
            }

            // 3. DataTransfer로 파일 주입
            if (input) {
                console.log('[NanoBanana] Found file input, injecting via DataTransfer...');
                const dataTransfer = new DataTransfer();
                dataTransfer.items.add(file);
                input.files = dataTransfer.files;

                // 이벤트 발생
                input.dispatchEvent(new Event('change', { bubbles: true }));
                input.dispatchEvent(new Event('input', { bubbles: true }));

                // 추가 이벤트 (일부 프레임워크 호환성)
                const dropEvent = new DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    dataTransfer: dataTransfer
                });
                input.dispatchEvent(dropEvent);

                await this.delay(1500);
                return { success: true, message: 'DataTransfer로 파일 주입 완료' };
            }

            // 4. 폴백: 드래그 앤 드롭 시뮬레이션 (입력창에 직접)
            console.log('[NanoBanana] File input still not found, trying drag & drop on editor...');

            // 드래그 앤 드롭 대상 확장
            const dropTargets = [
                // 1. 에디터 영역 (가장 유력)
                document.querySelector('.ql-editor'),
                document.querySelector('.rich-textarea'),
                document.querySelector('[contenteditable="true"]'),

                // 2. 입력 컨테이너
                document.querySelector('.input-area-container'),
                document.querySelector('.input-area'),
                document.querySelector('textarea-container'),
                document.querySelector('.text-input-wrapper'),
                document.querySelector('.text-input-field'),

                // 3. 전체 바디 (최후의 수단)
                document.body
            ].filter(Boolean);

            // 중복 제거
            const uniqueTargets = [...new Set(dropTargets)];

            for (const dropzone of uniqueTargets) {
                if (!this.isInteractable(dropzone)) continue;

                console.log(`[NanoBanana] Attempting drop on: ${dropzone.tagName}.${dropzone.className}`);

                try {
                    const dt = new DataTransfer();
                    dt.items.add(file);

                    // 포커스 시도
                    dropzone.focus();
                    await this.delay(100);

                    // 드래그 시작 이벤트
                    const dragStartEvent = new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragStartEvent);

                    await this.delay(50);

                    // 드래그 엔터
                    const dragEnterEvent = new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragEnterEvent);

                    await this.delay(50);

                    // 드래그 오버 (필수)
                    const dragOverEvent = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragOverEvent);

                    await this.delay(50);

                    // 드롭 (핵심)
                    const dropEvent = new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dropEvent);

                    // 드래그 종료
                    const dragEndEvent = new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragEndEvent);

                    // 입력 이벤트 발생 (React 상태 갱신 유도)
                    const inputEvent = new Event('input', { bubbles: true });
                    dropzone.dispatchEvent(inputEvent);

                    const changeEvent = new Event('change', { bubbles: true });
                    dropzone.dispatchEvent(changeEvent);

                    await this.delay(2000); // 처리 대기

                    // 성공 여부 확인
                    if (await this.waitForImageUpload(3000)) {
                        return { success: true, message: `드래그 앤 드롭(${dropzone.className})으로 업로드 성공` };
                    }
                } catch (err) {
                    console.warn(`[NanoBanana] Drop failed on ${dropzone.className}:`, err);
                }
            }

            return { success: false, message: '모든 파일 주입 시도(input/dropzone) 실패' };
        } catch (e) {
            console.error('[NanoBanana] Upload error:', e);
            return { success: false, message: e.message };
        }
    },

    /**
     * 마지막 생성 이미지의 Base64 추출 (CORS 회피 시도)
     */
    getGeneratedImageBase64: async function () {
        try {
            const img = document.querySelector('.model-response img[src*="googleusercontent"], .generated-image img');
            if (!img || !img.src) return { success: false, message: '이미지 없음' };

            const src = img.src;
            console.log(`[NanoBanana] Extracting image: ${src.substring(0, 50)}...`);

            if (src.startsWith('blob:')) {
                const res = await fetch(src);
                const blob = await res.blob();
                return new Promise(resolve => {
                    const reader = new FileReader();
                    reader.onloadend = () => resolve({ success: true, base64: reader.result });
                    reader.readAsDataURL(blob);
                });
            }

            return { success: true, base64: src, message: 'URL 반환' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    /**
     * 다운로드 버튼 활성화 대기 (강화된 호버 및 감지)
     */
    waitForDownloadReady: async function (timeout = 30000) {
        const startTime = Date.now();
        console.log('[NanoBanana] Waiting for download button readiness...');

        while (Date.now() - startTime < timeout) {
            const btn = document.querySelector('button[aria-label*="다운로드"], button[aria-label*="Download"], button.generated-image-button');
            if (btn && this.isInteractable(btn)) return { success: true, message: '다운로드 준비됨' };

            // 호버 유도
            const imgs = document.querySelectorAll('button.image-button img, .model-response img');
            if (imgs.length > 0) {
                const lastImg = imgs[imgs.length - 1];
                lastImg.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            }

            await this.delay(800);
        }

        return { success: false, message: '다운로드 대기 타임아웃' };
    },

    /**
     * 완전 자동화 워크플로우 (CDP 연동용)
     * 이미지 업로드부터 결과 추출까지 한 번에 처리
     * @param {string} prompt - 이미지 처리 프롬프트
     * @param {string} imageBase64 - Base64 인코딩된 이미지
     * @param {string} filename - 원본 파일 이름
     * @param {boolean} useProMode - Pro 모드 사용 여부
     * @returns {Promise<{success: boolean, resultBase64: string|null, steps: Array, message: string}>}
     */
    autoRunWorkflow: async function (prompt, imageBase64, filename, useProMode = true) {
        const steps = [];
        let resultBase64 = null;

        console.log('[NanoBanana] ===== autoRunWorkflow 시작 =====');
        console.log(`[NanoBanana] 파일: ${filename}, Pro모드: ${useProMode}`);

        try {
            // 0. 페이지 준비 상태 확인
            console.log('[NanoBanana] [0/8] 페이지 준비 상태 확인...');

            // 전역 팝업(면책 조항 등) 먼저 처리
            await this.handleDisclaimer();

            const inputExists = document.querySelector('.ql-editor, [contenteditable="true"]');
            if (!inputExists) {
                console.error('[NanoBanana] 페이지가 준비되지 않음');
                return { success: false, resultBase64: null, steps, message: '페이지가 준비되지 않음' };
            }

            // 1. Pro 모드 선택
            if (useProMode) {
                console.log('[NanoBanana] [1/8] Pro 모드 선택...');
                const proResult = await this.selectProMode();
                steps.push({ step: 'Pro 모드', ...proResult });
                console.log(`[NanoBanana] Pro 모드 결과: ${proResult.success ? '성공' : '실패'}`);
                await this.delay(500);
            }

            // 2. 이미지 생성 도구 활성화
            console.log('[NanoBanana] [2/8] 이미지 생성 모드 활성화...');
            const imgGenResult = await this.enableImageGeneration();
            steps.push({ step: '이미지 생성 모드', ...imgGenResult });
            console.log(`[NanoBanana] 이미지 생성 모드 결과: ${imgGenResult.success ? '성공' : '실패'}`);
            await this.delay(500);

            // 3. 업로드 메뉴 열기 (선택 사항 - 메뉴 열기 실패해도 input이 있으면 진행 가능)
            console.log('[NanoBanana] [3/8] 업로드 메뉴 열기...');
            const menuResult = await this.openUploadMenu();
            steps.push({ step: '업로드 메뉴', ...menuResult });

            // 메뉴 열기 실패하더라도 input[type=file]이 존재할 수 있으므로 치명적 오류로 처리하지 않음
            if (!menuResult.success) {
                console.warn('[NanoBanana] 업로드 메뉴 열기 실패했으나 업로드 시도 계속...');
            }
            await this.delay(500);

            // 4. 이미지 자동 업로드
            console.log('[NanoBanana] [4/8] 이미지 업로드...');
            const uploadResult = await this.uploadImageFromPath(imageBase64, filename);
            steps.push({ step: '이미지 업로드', ...uploadResult });
            if (!uploadResult.success) {
                console.error('[NanoBanana] 이미지 업로드 실패');
                return { success: false, resultBase64: null, steps, message: '이미지 업로드 실패' };
            }

            // 업로드 완료 확인 대기
            console.log('[NanoBanana] [4.5/8] 업로드 완료 대기...');
            const uploadConfirmed = await this.waitForImageUpload(30000);
            if (!uploadConfirmed) {
                steps.push({ step: '업로드 확인', success: false, message: '업로드 확인 타임아웃' });
                console.error('[NanoBanana] 업로드 확인 타임아웃');
                return { success: false, resultBase64: null, steps, message: '이미지 업로드 확인 실패' };
            }
            steps.push({ step: '업로드 확인', success: true, message: '업로드 확인됨' });

            // 5. 프롬프트 입력
            console.log('[NanoBanana] [5/8] 프롬프트 입력...');
            const promptResult = await this.writePrompt(prompt);
            steps.push({ step: '프롬프트 입력', ...promptResult });
            if (!promptResult.success) {
                console.error('[NanoBanana] 프롬프트 입력 실패');
                return { success: false, resultBase64: null, steps, message: '프롬프트 입력 실패' };
            }
            await this.delay(500);

            // 6. 메시지 전송
            console.log('[NanoBanana] [6/8] 메시지 전송...');
            const sendResult = await this.sendMessage(60000);
            steps.push({ step: '메시지 전송', ...sendResult });
            if (!sendResult.success) {
                console.error('[NanoBanana] 메시지 전송 실패');
                return { success: false, resultBase64: null, steps, message: '메시지 전송 실패' };
            }

            // 7. 응답 대기
            console.log('[NanoBanana] [7/8] 응답 대기 (최대 3분)...');
            const responseResult = await this.waitForResponse(180000);
            steps.push({ step: '응답 대기', ...responseResult });
            if (!responseResult.success) {
                console.error('[NanoBanana] 응답 대기 실패: ' + responseResult.message);
                return { success: false, resultBase64: null, steps, message: responseResult.message };
            }
            console.log(`[NanoBanana] 응답 완료, 이미지 포함: ${responseResult.hasImage}`);

            // 8. 이미지 추출 (이미지가 있는 경우)
            if (responseResult.hasImage) {
                console.log('[NanoBanana] [8/8] 이미지 추출...');
                await this.delay(2000); // 이미지 렌더링 대기

                const extractResult = await this.getGeneratedImageBase64();
                steps.push({ step: '이미지 추출', ...extractResult });

                if (extractResult.success && extractResult.base64) {
                    resultBase64 = extractResult.base64;
                    console.log('[NanoBanana] 이미지 추출 성공');
                } else {
                    console.warn('[NanoBanana] 이미지 추출 실패 또는 Base64 없음');
                }
            }

            console.log('[NanoBanana] ===== autoRunWorkflow 완료 =====');
            return {
                success: true,
                resultBase64,
                steps,
                message: resultBase64 ? '워크플로우 완료 (이미지 추출됨)' : '워크플로우 완료 (텍스트 응답)'
            };

        } catch (e) {
            console.error('[NanoBanana] autoRunWorkflow 오류:', e);
            return { success: false, resultBase64: null, steps, message: e.message };
        }
    },

    // ========== 전체 워크플로우 (한 번에 실행) ==========

    /**
     * 전체 NanoBanana 이미지 처리 워크플로우
     * @param {string} prompt - 이미지 처리 프롬프트
     * @param {boolean} useProMode - Pro 모드 사용 여부
     * @param {boolean} useImageGen - 이미지 생성 도구 활성화 여부
     * @returns {Promise<{success: boolean, steps: Array, message: string}>}
     */
    runWorkflow: async function (prompt, useProMode = true, useImageGen = true) {
        const steps = [];

        try {
            // 1. Pro 모드 선택
            if (useProMode) {
                const proResult = await this.selectProMode();
                steps.push({ step: 'Pro 모드', ...proResult });
                if (!proResult.success) {
                    return { success: false, steps, message: 'Pro 모드 활성화 실패' };
                }
            }

            // 2. 이미지 생성 모드 활성화
            if (useImageGen) {
                const imgGenResult = await this.enableImageGeneration();
                steps.push({ step: '이미지 생성 모드', ...imgGenResult });
                // 실패해도 계속 진행 (이미 활성화되어 있을 수 있음)
            }

            // 3. 업로드 메뉴 열기
            const uploadResult = await this.openUploadMenu();
            steps.push({ step: '업로드 메뉴', ...uploadResult });

            // 4. 이미지 업로드 대기 (수동 선택 필요)
            steps.push({ step: '이미지 업로드', success: true, message: '수동 이미지 선택 대기...' });
            const uploadComplete = await this.waitForImageUpload(120000);
            if (!uploadComplete) {
                return { success: false, steps, message: '이미지 업로드 타임아웃' };
            }
            steps.push({ step: '업로드 확인', success: true, message: '이미지 업로드 완료' });

            // 5. 프롬프트 입력
            const promptResult = await this.writePrompt(prompt);
            steps.push({ step: '프롬프트 입력', ...promptResult });
            if (!promptResult.success) {
                return { success: false, steps, message: '프롬프트 입력 실패' };
            }

            // 6. 메시지 전송
            const sendResult = await this.sendMessage();
            steps.push({ step: '메시지 전송', ...sendResult });
            if (!sendResult.success) {
                return { success: false, steps, message: '메시지 전송 실패' };
            }

            // 7. 응답 대기
            const responseResult = await this.waitForResponse();
            steps.push({ step: '응답 대기', ...responseResult });
            if (!responseResult.success) {
                return { success: false, steps, message: responseResult.message };
            }

            // 8. 이미지 다운로드 (이미지가 있는 경우)
            if (responseResult.hasImage) {
                const downloadResult = await this.downloadOriginalImage();
                steps.push({ step: '이미지 다운로드', ...downloadResult });
            }

            return { success: true, steps, message: '워크플로우 완료' };

        } catch (e) {
            return { success: false, steps, message: e.message };
        }
    }
};

// 전역으로 노출
window.NanoBanana = NanoBanana;

// 로드 확인
console.log('%c🍌 NanoBanana Automation Loaded', 'background: #130, 70, 160; color: white; font-size: 14px; padding: 5px;');
