/**
 * NanoBanana Gemini Automation Scripts
 * 
 * CDP를 통해 Gemini 웹페이지에 주입되어 이미지 처리 자동화를 수행합니다.
 * EdgeCdpAutomation.cs에서 EvaluateFunctionAsync로 호출됩니다.
 * 
 * 의존성: GeminiCommon.js (먼저 로드되어야 함)
 * 사용법: 각 함수를 CDP를 통해 개별적으로 호출
 */

const NanoBanana = {
    // GeminiCommon 참조 (편의용)
    get common() {
        return window.GeminiCommon;
    },

    // ========== 페이지 상태 체크 및 오류 복구 ==========

    /**
     * 페이지 상태 확인 - 500 에러, 로딩 상태 등 감지
     * @returns {{healthy: boolean, errorType: string|null, message: string, needsWait: boolean}}
     */
    checkPageHealth: function () {
        // 1. Google 500 에러 페이지 감지
        const pageText = document.body?.innerText || '';
        const pageTitle = document.title || '';

        // "500. 오류가 발생했습니다" 패턴
        if (pageText.includes('500.') && (pageText.includes('오류') || pageText.includes('error'))) {
            return { healthy: false, errorType: 'server_500', message: 'Gemini 500 서버 오류', needsWait: true };
        }

        // "Error 500" 또는 유사 패턴
        if (pageTitle.includes('500') || pageTitle.includes('Error') || pageTitle.includes('오류')) {
            return { healthy: false, errorType: 'server_error', message: '서버 오류 페이지', needsWait: true };
        }

        // "문제가 발생했습니다" 또는 "Something went wrong"
        if (pageText.includes('문제가 발생') || pageText.includes('Something went wrong')) {
            return { healthy: false, errorType: 'general_error', message: '일반 오류', needsWait: true };
        }

        // "나중에 다시 시도" 메시지
        if (pageText.includes('나중에 다시 시도') || pageText.includes('try again later')) {
            return { healthy: false, errorType: 'rate_limit', message: '일시적 서비스 불가', needsWait: true };
        }

        // *** Gemini 로딩 페이지 감지 ("Gemini 3에게 물어보기" 등) ***
        const hasLoadingPlaceholder = pageText.includes('물어보기') ||
            pageText.includes('Ask Gemini') ||
            pageText.includes('Gemini에게');
        const inputArea = document.querySelector('.ql-editor, [contenteditable="true"]');
        const inputText = inputArea?.innerText?.trim() || '';

        // 입력창이 있지만 플레이스홀더만 있는 상태 (로딩 중)
        if (hasLoadingPlaceholder && (!inputArea || inputText === '' || inputText.includes('물어보기'))) {
            // 히스토리/채팅 목록이 로드되었는지 확인
            const hasChatHistory = !!document.querySelector('[data-conversation-id], .conversation-container, .chat-history');
            if (!hasChatHistory) {
                return { healthy: false, errorType: 'loading', message: 'Gemini 로딩 중...', needsWait: true };
            }
        }

        // 정상 페이지 확인 (입력창 존재 여부)
        const hasInput = !!inputArea;
        if (!hasInput && window.location.hostname.includes('gemini.google.com')) {
            // Gemini 페이지인데 입력창이 없으면 로딩 중이거나 오류
            return { healthy: false, errorType: 'loading_or_error', message: '페이지 준비 안됨', needsWait: true };
        }

        return { healthy: true, errorType: null, message: '정상', needsWait: false };
    },

    /**
     * 페이지가 완전히 로드될 때까지 대기
     * @param {number} maxWaitMs - 최대 대기 시간 (밀리초)
     * @param {number} checkIntervalMs - 확인 간격 (밀리초)
     * @returns {Promise<{ready: boolean, waitedMs: number, lastState: string}>}
     */
    waitForPageReady: async function (maxWaitMs = 30000, checkIntervalMs = 1000) {
        const common = this.common;
        const startTime = Date.now();
        let lastState = '';

        console.log('[NanoBanana] 페이지 로딩 대기 시작...');

        while (Date.now() - startTime < maxWaitMs) {
            const health = this.checkPageHealth();
            lastState = health.message;

            if (health.healthy) {
                const waitedMs = Date.now() - startTime;
                console.log(`[NanoBanana] 페이지 준비 완료 (${waitedMs}ms 대기)`);
                return { ready: true, waitedMs, lastState: '정상' };
            }

            // 복구 불가능한 에러인 경우 즉시 종료
            if (health.errorType === 'server_500' || health.errorType === 'rate_limit') {
                console.warn(`[NanoBanana] 복구 불가능한 오류: ${health.message}`);
                return { ready: false, waitedMs: Date.now() - startTime, lastState: health.message };
            }

            // 진행률 로깅 (5초마다)
            const elapsed = Date.now() - startTime;
            if (elapsed % 5000 < checkIntervalMs) {
                console.log(`[NanoBanana] 대기 중... ${Math.floor(elapsed / 1000)}초 경과 (${health.message})`);
            }

            await common.delay(checkIntervalMs);
        }

        console.warn(`[NanoBanana] 페이지 로딩 타임아웃 (${maxWaitMs}ms)`);
        return { ready: false, waitedMs: maxWaitMs, lastState };
    },

    /**
     * 페이지 오류 시 자동 복구
     * @param {number} maxRetries - 최대 재시도 횟수
     * @returns {Promise<{success: boolean, message: string}>}
     */
    recoverFromError: async function (maxRetries = 3) {
        const common = this.common;
        console.log('[NanoBanana] 페이지 오류 복구 시도...');

        // 먼저 로딩 대기 시도
        const waitResult = await this.waitForPageReady(15000, 1000);
        if (waitResult.ready) {
            return { success: true, message: '로딩 대기 후 정상화됨' };
        }

        for (let attempt = 1; attempt <= maxRetries; attempt++) {
            console.log(`[NanoBanana] 복구 시도 ${attempt}/${maxRetries}`);

            // 새 페이지로 이동
            window.location.href = 'https://gemini.google.com/app';

            // 페이지 로딩 대기
            await common.delay(3000);

            // 로딩 완료 대기
            const loadResult = await this.waitForPageReady(10000, 1000);
            if (loadResult.ready) {
                console.log('[NanoBanana] 페이지 복구 성공!');
                return { success: true, message: '페이지 복구 완료' };
            }

            // 추가 대기 후 재시도
            await common.delay(2000);
        }

        return { success: false, message: '페이지 복구 실패 - 수동 개입 필요' };
    },

    // ========== 모드 및 환경 설정 ==========

    /**
     * Pro 모드 활성화 (강력한 선택자 적용)
     */
    selectProMode: async function () {
        try {
            console.log('[NanoBanana] Attempting to select Pro mode...');
            const common = this.common;

            // 전역 팝업 처리
            await common.handleDisclaimer();

            // 1. 현재 모드 확인 (이미 Pro인지 체크)
            const currentModeText = document.querySelector('.input-area-switch text, .input-area-switch .mat-mdc-button-touch-target')?.parentElement?.innerText || '';
            if (currentModeText.toLowerCase().includes('pro')) {
                console.log('[NanoBanana] Already in Pro mode.');
                return { success: true, message: '이미 Pro 모드입니다' };
            }

            // 2. 모드 메뉴 열기
            const modeBtn = await common.waitForElement('button.input-area-switch, button[aria-label*="모드"]');
            if (!modeBtn) return { success: false, message: '모드 선택 버튼을 찾을 수 없습니다' };

            common.safeClick(modeBtn);
            await common.delay(500); // Python 타이밍 참조: 500ms

            // 3. Pro 옵션 선택 (Shadow DOM 및 다중 선택자)
            const menuItems = Array.from(document.querySelectorAll('button[role="menuitemradio"], button.mat-mdc-menu-item, .mat-mdc-menu-content button, button.bard-mode-list-button'));
            const proItem = menuItems.find(item => item.innerText.includes('Pro') || item.innerText.includes('프로'));

            if (proItem) {
                common.safeClick(proItem);
                await common.delay(500); // Python 타이밍 참조: 500ms
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
            const common = this.common;

            // 전역 팝업 처리
            await common.handleDisclaimer();

            // 1. 도구 버튼 찾기
            const toolsBtn = await common.waitForElement('button.toolbox-drawer-button, button[aria-label*="도구"], button[aria-label*="Tools"]');
            if (!toolsBtn) return { success: false, message: '도구 버튼을 찾을 수 없습니다' };

            common.safeClick(toolsBtn);
            await common.delay(1000);

            // 2. 이미지 생성하기 옵션 (Aria-label 및 텍스트 조합)
            const allItems = Array.from(document.querySelectorAll('button, .mat-mdc-list-item, [role="menuitem"]'));
            const targetItem = allItems.find(item =>
                item.innerText.includes('이미지 생성') ||
                item.innerText.includes('Create image') ||
                item.getAttribute('aria-label')?.includes('이미지 생성')
            );

            if (targetItem) {
                common.safeClick(targetItem);
                await common.delay(800);
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

    /**
     * 파일 업로드 메뉴 열기 (네이티브 파일 다이얼로그 트리거 방지)
     * 주의: 실제 파일 주입은 uploadImageFromPath에서 DataTransfer로 수행
     */
    openUploadMenu: async function () {
        try {
            console.log('[NanoBanana] Opening upload menu (dialog-free mode)...');
            const common = this.common;

            // 전역 팝업 처리
            await common.handleDisclaimer();

            // 업로드 버튼 찾기 (메뉴만 열기 위함)
            const uploadSelectors = [
                'button[aria-label*="업로드"]',
                'button[aria-label*="upload"]',
                'button.upload-card-button'
            ];

            let menuBtn = null;
            for (const sel of uploadSelectors) {
                menuBtn = document.querySelector(sel);
                if (menuBtn && common.isInteractable(menuBtn)) break;
            }

            if (!menuBtn) {
                // 업로드 버튼이 없어도 입력창에 직접 드롭이 가능할 수 있으므로 성공으로 처리
                console.log('[NanoBanana] No upload button found, will attempt direct drop.');
                return { success: true, message: '업로드 버튼 없음 (직접 드롭 시도)' };
            }

            common.safeClick(menuBtn);
            await common.delay(500);

            // 클릭 후에도 면책 조항이 뜨면 한 번 더 체크
            await common.handleDisclaimer();

            // [중요] 파일 업로드 서브메뉴 클릭 생략 - 네이티브 다이얼로그 방지
            // 실제 파일 주입은 uploadImageFromPath()에서 DataTransfer로 수행함
            console.log('[NanoBanana] Upload menu opened. Skipping file dialog trigger.');

            // ESC로 메뉴 닫기 (드롭존만 활성화하고 다이얼로그 방지)
            await common.delay(300);
            document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

            return { success: true, message: '업로드 메뉴 오픈됨 (다이얼로그 없음)' };
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
        const common = this.common;
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

            await common.delay(300);
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
            const common = this.common;
            const input = await common.waitForElement('.ql-editor, [contenteditable="true"]');

            if (!input) return { success: false, message: '입력창을 찾을 수 없습니다' };

            input.focus();

            // 1. execCommand 로 물리적 타이핑 시뮬레이션
            document.execCommand('selectAll', false, null);
            document.execCommand('delete', false, null);
            await common.delay(100);
            document.execCommand('insertText', false, text);

            // 2. React/Angular 상태 업데이트 유도 (이벤트 강제 발생)
            const events = ['input', 'change', 'blur'];
            events.forEach(name => {
                input.dispatchEvent(new Event(name, { bubbles: true }));
            });

            await common.delay(300);
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
            const common = this.common;
            const startTime = Date.now();

            while (Date.now() - startTime < timeout) {
                // 전송 버튼 선택자 (Aria-label 기반이 가장 정확)
                const sendBtn = document.querySelector('button.send-button, button[aria-label*="보내기"], button[aria-label*="Send"]');

                if (sendBtn && common.isInteractable(sendBtn)) {
                    // 비활성화 여부 재확인 (React 상태 대기)
                    if (sendBtn.getAttribute('aria-disabled') !== 'true') {
                        common.safeClick(sendBtn);
                        return { success: true, message: '메시지 전송 성공' };
                    }
                }
                await common.delay(400);
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
     * 응답 생성 완료 대기 (이미지 생성 전용) - 2026.01 강화 버전
     * 오류 상태 감지, 진행률 로깅, 연결 끊김 방지 추가
     */
    waitForResponse: async function (timeout = 180000) {
        console.log('[NanoBanana] Waiting for AI response...');
        const common = this.common;
        const startTime = Date.now();
        let lastResponseText = '';
        let stableCount = 0;
        let lastProgressLog = 0;

        while (Date.now() - startTime < timeout) {
            const elapsed = Math.floor((Date.now() - startTime) / 1000);

            // 진행률 로깅 (10초마다)
            if (elapsed - lastProgressLog >= 10) {
                console.log(`[NanoBanana] 응답 대기 중... (${elapsed}초 경과)`);
                lastProgressLog = elapsed;
            }

            // 오류 상태 감지 (Rate limit, Error 등)
            const errorCheck = (function () {
                // 오류 메시지 탐지
                const errorSelectors = [
                    '.error-message',
                    '.rate-limit-message',
                    '[class*="error"]',
                    '.snackbar-error'
                ];
                for (const sel of errorSelectors) {
                    const el = document.querySelector(sel);
                    if (el && el.offsetParent !== null && el.innerText) {
                        const text = el.innerText.toLowerCase();
                        if (text.includes('error') || text.includes('오류') ||
                            text.includes('limit') || text.includes('제한') ||
                            text.includes('try again') || text.includes('다시 시도')) {
                            return { hasError: true, message: el.innerText.substring(0, 100) };
                        }
                    }
                }

                // 응답 내용에서 오류 탐지
                const lastResponse = document.querySelector('.model-response-text:last-child, .markdown:last-child');
                if (lastResponse) {
                    const text = lastResponse.innerText.toLowerCase();
                    if (text.includes('i cannot') || text.includes('i can\'t') ||
                        text.includes('unable to') || text.includes('할 수 없')) {
                        return { hasError: true, message: '생성 불가 응답 감지' };
                    }
                }

                return { hasError: false };
            })();

            if (errorCheck.hasError) {
                console.error(`[NanoBanana] 오류 감지: ${errorCheck.message}`);
                return { success: false, hasImage: false, message: `오류: ${errorCheck.message}`, errorType: 'response_error' };
            }

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
                await common.delay(1000); // 1초로 단축
                continue;
            }

            // 2. 응답 내용 추출 및 안정성 확인
            const responseElements = document.querySelectorAll('.model-response-text, .markdown:not(.user-prompt)');
            const currentResponse = responseElements.length > 0 ? responseElements[responseElements.length - 1].innerText : '';

            if (currentResponse && currentResponse === lastResponseText) {
                stableCount++;
                // 3회 연속(약 3초) 변화 없으면 완료
                if (stableCount >= 3) {
                    const hasImage = !!document.querySelector("img[src*='googleusercontent'], .generated-image, model-response img");
                    console.log(`[NanoBanana] 응답 완료 (${elapsed}초), 이미지: ${hasImage}`);
                    return { success: true, hasImage, message: '응답 생성 완료' };
                }
            } else {
                stableCount = 0;
                lastResponseText = currentResponse;
            }

            await common.delay(1000); // 1초로 단축
        }

        // 타임아웃 시 응답 중지 시도
        console.warn('[NanoBanana] 응답 대기 타임아웃 - 중지 시도...');
        this.stopGeminiResponse();

        return { success: false, hasImage: false, message: '응답 대기 시간 초과', errorType: 'timeout' };
    },

    /**
     * 생성된 이미지 원본 크기 다운로드 (강화된 호버 및 감지)
     */
    downloadOriginalImage: async function () {
        try {
            console.log('[NanoBanana] Searching for generated image to download...');
            const common = this.common;

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
                    if (common.isInteractable(targetImg)) break;
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
            await common.delay(1000);

            // 3. 다운로드 버튼 선택 (다중 선택자)
            const downloadBtn = await common.waitForElement('button[aria-label*="다운로드"], button[aria-label*="Download"], button.generated-image-button, .on-hover-button button');
            if (downloadBtn) {
                console.log('[NanoBanana] Download button found, clicking...');
                common.safeClick(downloadBtn);
                return { success: true, message: '다운로드 시작됨' };
            }

            // 버튼이 안 보이면 직접 부모 레이어에서 찾기
            const parentContainer = targetImg.closest('.model-response, .chat-history, .response-container');
            if (parentContainer) {
                const fallbackBtn = parentContainer.querySelector('button[aria-label*="다운로드"]');
                if (fallbackBtn) {
                    common.safeClick(fallbackBtn);
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
     * 현재 채팅 삭제 - 2026.01 대기시간 최적화
     */
    deleteCurrentChat: async function () {
        try {
            console.log('[NanoBanana] Deleting current chat...');
            const common = this.common;
            const menuBtn = await common.waitForElement('button[aria-label*="대화 작업"], button[aria-label*="actions"]');
            if (!menuBtn) return { success: false, message: '메뉴 버튼 없음' };

            common.safeClick(menuBtn);
            await common.delay(400); // 600ms → 400ms

            const deleteItem = Array.from(document.querySelectorAll('[role="menuitem"], button.mat-mdc-menu-item'))
                .find(el => el.innerText.includes('삭제') || el.innerText.includes('Delete'));

            if (!deleteItem) {
                document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                return { success: false, message: '삭제 항목 없음' };
            }

            common.safeClick(deleteItem);
            await common.delay(400); // 800ms → 400ms

            const confirmBtn = Array.from(document.querySelectorAll('mat-dialog-actions button, .mat-mdc-dialog-actions button'))
                .find(el => el.innerText.includes('삭제') || el.innerText.includes('Delete'));

            if (confirmBtn) {
                common.safeClick(confirmBtn);
                await common.delay(500); // 1000ms → 500ms
                return { success: true, message: '삭제 완료' };
            }

            return { success: false, message: '확인 버튼 없음' };
        } catch (e) {
            return { success: false, message: e.message };
        }
    },

    /**
     * Gemini 응답 생성 중지
     * 현재 진행 중인 AI 응답 생성을 중지합니다.
     */
    stopGeminiResponse: function () {
        try {
            console.log('[NanoBanana] Attempting to stop Gemini response...');
            const common = this.common;

            // 1. Send 버튼이 Stop 상태인 경우 (가장 일반적)
            const sendBtn = document.querySelector('.send-button.stop');
            if (sendBtn && sendBtn.offsetParent !== null && !sendBtn.disabled) {
                common.safeClick(sendBtn);
                console.log('[NanoBanana] Stopped via send button');
                return { success: true, message: 'send 버튼으로 중지됨' };
            }

            // 2. 별도의 중지 버튼 검색
            const stopSelectors = [
                'button[aria-label*="중지"]',
                'button[aria-label*="Stop"]',
                'button[aria-label="대답 생성 중지"]',
                'button[aria-label="Stop generating"]'
            ];

            for (const sel of stopSelectors) {
                const btn = document.querySelector(sel);
                if (btn && btn.offsetParent !== null && !btn.disabled) {
                    common.safeClick(btn);
                    console.log(`[NanoBanana] Stopped via ${sel}`);
                    return { success: true, message: `${sel}로 중지됨` };
                }
            }

            // 3. mat-icon으로 stop 검색
            const icons = document.querySelectorAll('mat-icon');
            for (const icon of icons) {
                if (icon.textContent === 'stop' || icon.textContent === 'stop_circle') {
                    const parentBtn = icon.closest('button');
                    if (parentBtn && parentBtn.offsetParent !== null && !parentBtn.disabled) {
                        common.safeClick(parentBtn);
                        console.log('[NanoBanana] Stopped via mat-icon');
                        return { success: true, message: 'mat-icon으로 중지됨' };
                    }
                }
            }

            console.log('[NanoBanana] No stop button found');
            return { success: false, message: '중지 버튼을 찾을 수 없음' };
        } catch (e) {
            console.error('[NanoBanana] Stop error:', e);
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

    // ========== 파일 전송 (Base64 직접 주입) ==========

    /**
     * Base64 이미지를 File 객체로 변환하여 input에 주입 (개선된 버전)
     */
    uploadImageFromPath: async function (base64Data, filename) {
        try {
            console.log(`[NanoBanana] Injecting image file: ${filename}`);
            const common = this.common;

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

            // 2. 드롭존 활성화를 위해 업로드 메뉴 열기 (다이얼로그 없이)
            // 기존 input이 없으면 드래그 앤 드롭으로 처리
            let input = document.querySelector('input[type="file"]');
            if (!input) {
                console.log('[NanoBanana] File input not found, preparing dropzone...');

                // 업로드 버튼 클릭 후 바로 ESC (다이얼로그 방지)
                const uploadBtn = document.querySelector('button[aria-label*="업로드"], button[aria-label*="upload"], button.upload-card-button');
                if (uploadBtn) {
                    common.safeClick(uploadBtn);
                    await common.delay(500);

                    // [중요] 파일 업로드 서브메뉴 클릭 생략 - 네이티브 다이얼로그 방지
                    // ESC로 메뉴 닫기
                    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                    await common.delay(300);
                }

                // 숨겨진 input 확인 (이미 존재할 수 있음)
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

                await common.delay(1500);
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
                if (!common.isInteractable(dropzone)) continue;

                console.log(`[NanoBanana] Attempting drop on: ${dropzone.tagName}.${dropzone.className}`);

                try {
                    const dt = new DataTransfer();
                    dt.items.add(file);

                    // 포커스 시도
                    dropzone.focus();
                    await common.delay(100);

                    // 드래그 시작 이벤트
                    const dragStartEvent = new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragStartEvent);

                    await common.delay(50);

                    // 드래그 엔터
                    const dragEnterEvent = new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragEnterEvent);

                    await common.delay(50);

                    // 드래그 오버 (필수)
                    const dragOverEvent = new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer: dt });
                    dropzone.dispatchEvent(dragOverEvent);

                    await common.delay(50);

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

                    await common.delay(2000); // 처리 대기

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
     * 마지막 생성 이미지의 Base64 추출 (강화된 버전 - 다중 선택자 + Canvas 폴백)
     */
    getGeneratedImageBase64: async function () {
        try {
            const common = this.common;

            // 최소 이미지 크기 (placeholder 및 아이콘 필터링)
            const MIN_WIDTH = 100;
            const MIN_HEIGHT = 100;

            // 다중 이미지 선택자 (우선순위 순)
            const imgSelectors = [
                // 1. googleusercontent (가장 일반적인 생성 이미지 URL)
                'img[src*="googleusercontent"]',
                '.model-response img[src*="googleusercontent"]',
                'message-content img[src*="googleusercontent"]',

                // 2. 응답 컨테이너 내 이미지
                '.model-response img',
                '.response-container img',
                'message-content img',

                // 3. 이미지 버튼 내 이미지 (제외 항목 아래에 처리)
                'button.image-button img',
                '.generated-image img',

                // 4. blob URL 이미지
                "img[src^='blob:']",

                // 5. data URL 이미지
                "img[src^='data:']"
            ];

            let targetImg = null;
            let imgSrc = null;

            for (const sel of imgSelectors) {
                const imgs = document.querySelectorAll(sel);
                // 역순으로 가장 최신 이미지부터 검색
                for (let i = imgs.length - 1; i >= 0; i--) {
                    const img = imgs[i];
                    if (!img || !img.src) continue;

                    // 크기 검증 (placeholder 필터링)
                    const width = img.naturalWidth || img.width;
                    const height = img.naturalHeight || img.height;

                    if (width < MIN_WIDTH || height < MIN_HEIGHT) {
                        console.log(`[NanoBanana] Skipping small image: ${width}x${height}`);
                        continue;
                    }

                    // 아이콘/placeholder URL 패턴 제외
                    const src = img.src.toLowerCase();
                    if (src.includes('icon') ||
                        src.includes('avatar') ||
                        src.includes('logo') ||
                        src.includes('placeholder') ||
                        src.includes('/s16/') ||
                        src.includes('/s24/') ||
                        src.includes('/s32/') ||
                        src.includes('/s48/') ||
                        src.includes('/s64/')) {
                        console.log(`[NanoBanana] Skipping icon/avatar image: ${src.substring(0, 60)}...`);
                        continue;
                    }

                    targetImg = img;
                    imgSrc = img.src;
                    console.log(`[NanoBanana] Valid image found via selector: ${sel} (${width}x${height})`);
                    break;
                }
                if (targetImg) break;
            }

            if (!targetImg || !imgSrc) {
                return { success: false, message: '유효한 이미지를 찾을 수 없음 (크기 조건 미충족 또는 0개 이미지)' };
            }

            console.log(`[NanoBanana] Extracting image: ${imgSrc.substring(0, 60)}...`);

            // data: URI인 경우 바로 반환
            if (imgSrc.startsWith('data:')) {
                console.log('[NanoBanana] Image is data URL, returning directly');
                return { success: true, base64: imgSrc };
            }

            // 방법 1: 이미 로드된 이미지에서 Canvas 추출 (가장 안정적)
            try {
                console.log('[NanoBanana] Attempting canvas extraction from loaded image...');
                const canvas = document.createElement('canvas');
                const ctx = canvas.getContext('2d');

                // 이미지 로드 완료 대기
                if (!targetImg.complete) {
                    await new Promise(resolve => {
                        targetImg.onload = resolve;
                        targetImg.onerror = resolve;
                        setTimeout(resolve, 3000);
                    });
                }

                canvas.width = targetImg.naturalWidth || targetImg.width;
                canvas.height = targetImg.naturalHeight || targetImg.height;

                if (canvas.width > 0 && canvas.height > 0) {
                    ctx.drawImage(targetImg, 0, 0);
                    const dataUrl = canvas.toDataURL('image/png');

                    // 유효한 이미지인지 확인 (빈 캔버스가 아닌지)
                    if (dataUrl.length > 100) {
                        console.log('[NanoBanana] Canvas extraction successful');
                        return { success: true, base64: dataUrl };
                    }
                }
            } catch (canvasErr) {
                console.log(`[NanoBanana] Canvas error (CORS): ${canvasErr.message}`);
            }

            // 방법 2: crossOrigin 속성으로 원본 크기 이미지 로드 후 Canvas 추출
            try {
                console.log('[NanoBanana] Attempting crossOrigin image load with original size...');

                // 원본 크기 URL로 변환 (=s0)
                let originalSizeUrl = imgSrc;
                if (imgSrc.includes('googleusercontent.com')) {
                    originalSizeUrl = imgSrc.replace(/=s\d+.*$|=w\d+.*$/, '=s0');
                    if (!originalSizeUrl.includes('=s0')) originalSizeUrl += '=s0';
                }
                console.log(`[NanoBanana] Original size URL: ${originalSizeUrl.substring(0, 80)}...`);

                const newImg = new Image();
                newImg.crossOrigin = 'anonymous';

                const imgLoaded = await new Promise((resolve) => {
                    newImg.onload = () => resolve(true);
                    newImg.onerror = () => resolve(false);
                    setTimeout(() => resolve(false), 8000); // 타임아웃 증가
                    newImg.src = originalSizeUrl;
                });

                if (imgLoaded && newImg.naturalWidth > 0) {
                    console.log(`[NanoBanana] Image loaded: ${newImg.naturalWidth}x${newImg.naturalHeight}`);
                    const canvas = document.createElement('canvas');
                    const ctx = canvas.getContext('2d');
                    canvas.width = newImg.naturalWidth;
                    canvas.height = newImg.naturalHeight;
                    ctx.drawImage(newImg, 0, 0);
                    const dataUrl = canvas.toDataURL('image/png');

                    if (dataUrl.length > 100) {
                        console.log('[NanoBanana] CrossOrigin canvas extraction successful');
                        return { success: true, base64: dataUrl };
                    }
                }
            } catch (crossErr) {
                console.log(`[NanoBanana] CrossOrigin error: ${crossErr.message}`);
            }

            // 방법 3: fetch로 원본 크기 이미지 다운로드 시도 (쿠키 포함)
            try {
                console.log('[NanoBanana] Attempting fetch with credentials for original size...');

                // 원본 크기 URL로 변환 (=s0)
                let fetchUrl = imgSrc;
                if (imgSrc.includes('googleusercontent.com')) {
                    fetchUrl = imgSrc.replace(/=s\d+.*$|=w\d+.*$/, '=s0');
                    if (!fetchUrl.includes('=s0')) fetchUrl += '=s0';
                }

                const res = await fetch(fetchUrl, {
                    credentials: 'include',
                    mode: 'cors'
                });
                if (res.ok) {
                    const blob = await res.blob();
                    console.log(`[NanoBanana] Fetch successful, blob size: ${blob.size} bytes`);
                    return new Promise(resolve => {
                        const reader = new FileReader();
                        reader.onloadend = () => {
                            console.log('[NanoBanana] Fetch base64 extraction complete');
                            resolve({ success: true, base64: reader.result });
                        };
                        reader.onerror = () => {
                            console.error('[NanoBanana] FileReader error');
                            resolve({ success: false, message: 'FileReader 오류' });
                        };
                        reader.readAsDataURL(blob);
                    });
                } else {
                    console.log(`[NanoBanana] Fetch failed: ${res.status}`);
                }
            } catch (fetchErr) {
                console.log(`[NanoBanana] Fetch error: ${fetchErr.message}`);
            }

            // 방법 4: 쿠키를 함께 전달하여 C#에서 다운로드 시도
            console.log('[NanoBanana] Fallback: returning URL for C# download');
            // URL을 원본 크기(=s0)로 변환
            let originalUrl = imgSrc;
            if (imgSrc.includes('googleusercontent.com')) {
                originalUrl = imgSrc.replace(/=s\d+.*$|=w\d+.*$/, '=s0');
                if (!originalUrl.includes('=s0')) originalUrl += '=s0';
            }
            return { success: true, base64: originalUrl, isUrl: true };

        } catch (e) {
            console.error('[NanoBanana] getGeneratedImageBase64 error:', e);
            return { success: false, message: e.message };
        }
    },

    /**
     * 다운로드 버튼 활성화 대기 (강화된 호버 및 감지)
     */
    waitForDownloadReady: async function (timeout = 30000) {
        const startTime = Date.now();
        const common = this.common;
        console.log('[NanoBanana] Waiting for download button readiness...');

        while (Date.now() - startTime < timeout) {
            const btn = document.querySelector('button[aria-label*="다운로드"], button[aria-label*="Download"], button.generated-image-button');
            if (btn && common.isInteractable(btn)) return { success: true, message: '다운로드 준비됨' };

            // 호버 유도
            const imgs = document.querySelectorAll('button.image-button img, .model-response img');
            if (imgs.length > 0) {
                const lastImg = imgs[imgs.length - 1];
                lastImg.dispatchEvent(new MouseEvent('mouseenter', { bubbles: true }));
            }

            await common.delay(800);
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
        const common = this.common;
        let resultBase64 = null;

        console.log('[NanoBanana] ===== autoRunWorkflow 시작 =====');
        console.log(`[NanoBanana] 파일: ${filename}, Pro모드: ${useProMode}`);

        try {
            // 0. 페이지 준비 상태 확인
            console.log('[NanoBanana] [0/8] 페이지 준비 상태 확인...');

            // 전역 팝업(면책 조항 등) 먼저 처리
            await common.handleDisclaimer();

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
                await common.delay(500);
            }

            // 2. 이미지 생성 도구 활성화
            console.log('[NanoBanana] [2/8] 이미지 생성 모드 활성화...');
            const imgGenResult = await this.enableImageGeneration();
            steps.push({ step: '이미지 생성 모드', ...imgGenResult });
            console.log(`[NanoBanana] 이미지 생성 모드 결과: ${imgGenResult.success ? '성공' : '실패'}`);
            await common.delay(500);

            // 3. 업로드 메뉴 열기 (선택 사항 - 메뉴 열기 실패해도 input이 있으면 진행 가능)
            console.log('[NanoBanana] [3/8] 업로드 메뉴 열기...');
            const menuResult = await this.openUploadMenu();
            steps.push({ step: '업로드 메뉴', ...menuResult });

            // 메뉴 열기 실패하더라도 input[type=file]이 존재할 수 있으므로 치명적 오류로 처리하지 않음
            if (!menuResult.success) {
                console.warn('[NanoBanana] 업로드 메뉴 열기 실패했으나 업로드 시도 계속...');
            }
            await common.delay(500);

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
            await common.delay(500);

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
                await common.delay(2000); // 이미지 렌더링 대기

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
        const common = this.common;

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
