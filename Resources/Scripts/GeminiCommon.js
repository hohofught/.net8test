/**
 * Gemini Common Utilities
 * 
 * 브라우저 모드와 NanoBanana에서 공유하는 유틸리티 함수 모음입니다.
 * 다른 스크립트에서 window.GeminiCommon으로 접근합니다.
 */

const GeminiCommon = {
    // ========== Shadow DOM 유틸리티 ==========

    /**
     * Shadow DOM 내부까지 탐색하는 단일 요소 선택자
     * @param {string} selector - >>> 로 구분된 Shadow DOM 선택자
     * @param {Element|Document} root - 탐색 시작점
     * @returns {Element|null}
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
     * @param {string} selector - >>> 로 구분된 Shadow DOM 선택자
     * @param {Element|Document} root - 탐색 시작점
     * @returns {Element[]}
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

    // ========== 요소 상태 확인 ==========

    /**
     * 요소가 가시적이고 상호작용 가능한지 확인
     * @param {Element} el - 확인할 요소
     * @returns {boolean}
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

    // ========== 비동기 유틸리티 ==========

    /**
     * Promise 기반 딜레이
     * @param {number} ms - 대기 시간(밀리초)
     * @returns {Promise<void>}
     */
    delay: function (ms) {
        return new Promise(r => setTimeout(r, ms));
    },

    /**
     * 요소가 나타날 때까지 대기 (Shadow DOM 지원)
     * @param {string} selector - CSS 선택자 (Shadow DOM은 >>> 사용)
     * @param {number} timeout - 타임아웃(밀리초)
     * @returns {Promise<Element|null>}
     */
    waitForElement: async function (selector, timeout = 15000) {
        const startTime = Date.now();
        console.log(`[GeminiCommon] Waiting for: ${selector}`);
        while (Date.now() - startTime < timeout) {
            const el = selector.includes('>>>')
                ? this.queryShadowSelector(selector)
                : document.querySelector(selector);
            if (el && this.isInteractable(el)) return el;
            await this.delay(300);
        }
        console.warn(`[GeminiCommon] Timeout waiting for: ${selector}`);
        return null;
    },

    // ========== 상호작용 유틸리티 ==========

    /**
     * 요소 클릭 (안전하게, 마우스 이벤트 포함)
     * @param {Element} element - 클릭할 요소
     * @returns {boolean} 성공 여부
     */
    safeClick: function (element) {
        if (!element) return false;
        try {
            console.log(`[GeminiCommon] Clicking element:`, element.tagName, element.className);
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
            console.error('[GeminiCommon] Click failed:', e);
            return false;
        }
    },

    // ========== Gemini 공통 기능 ==========

    /**
     * 면책 조항 또는 동의 팝업 처리
     * @returns {Promise<boolean>} 팝업을 처리했는지 여부
     */
    handleDisclaimer: async function () {
        const disclaimerButtons = Array.from(document.querySelectorAll('button')).filter(btn => {
            const txt = btn.innerText.toLowerCase();
            return (txt.includes('동의') || txt.includes('수락') || txt.includes('agree') || txt.includes('accept')) &&
                this.isInteractable(btn);
        });

        if (disclaimerButtons.length > 0) {
            console.log('[GeminiCommon] Disclaimer/Consent detected, clicking...');
            this.safeClick(disclaimerButtons[0]);
            await this.delay(1000);
            return true;
        }
        return false;
    },

    /**
     * 입력창 요소 찾기
     * @returns {Element|null}
     */
    getInputElement: function () {
        return document.querySelector('.ql-editor') ||
            document.querySelector('div[contenteditable="true"]') ||
            document.querySelector('rich-textarea .ql-editor');
    },

    /**
     * 전송 버튼 찾기
     * @returns {Element|null}
     */
    getSendButton: function () {
        // 1. 클래스로 찾기
        let btn = document.querySelector('.send-button:not(.stop)');
        if (btn && !btn.disabled) return btn;

        // 2. aria-label로 찾기
        const ariaLabels = ['보내기', 'Send message', '전송', '메시지 보내기'];
        for (const label of ariaLabels) {
            btn = document.querySelector(`button[aria-label="${label}"]`);
            if (btn && !btn.disabled) return btn;
        }

        // 3. mat-icon으로 찾기
        const icons = document.querySelectorAll('mat-icon');
        for (const icon of icons) {
            if (icon.textContent.trim() === 'send') {
                btn = icon.closest('button');
                if (btn && !btn.disabled) return btn;
            }
        }

        return null;
    },

    /**
     * 응답 요소들 찾기
     * @returns {NodeList}
     */
    getResponseElements: function () {
        return document.querySelectorAll('message-content.model-response-text, .model-response-text');
    },

    /**
     * 최신 응답 텍스트 가져오기 (이미지 응답 필터링 포함)
     * @returns {string}
     */
    getLatestResponse: function () {
        const responses = this.getResponseElements();
        if (responses.length === 0) return '';

        const lastResponse = responses[responses.length - 1];

        // 1. 마크다운 영역에서 추출
        const markdownEl = lastResponse.querySelector('.markdown');
        if (markdownEl) {
            const text = markdownEl.innerText || '';
            const cleaned = text.trim()
                .replace(/^image_generated\s*/gi, '')
                .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                .replace(/^\[Image[^\]]*\]\s*/gi, '');
            if (cleaned.length > 0) return cleaned;
        }

        // 2. innerText에서 이미지 관련 텍스트 필터링
        let text = (lastResponse.innerText || '').trim();
        text = text
            .replace(/^image_generated\s*/gi, '')
            .replace(/^\[이미지[^\]]*\]\s*/gi, '')
            .replace(/^\[Image[^\]]*\]\s*/gi, '');

        return text;
    },

    /**
     * 생성 중인지 확인
     * @returns {boolean}
     */
    isGenerating: function () {
        // 1. Stop 버튼 확인
        const sendBtn = document.querySelector('.send-button');
        if (sendBtn && sendBtn.classList.contains('stop')) return true;

        // 2. aria-busy 확인
        const lastMarkdown = [...document.querySelectorAll('.markdown')].pop();
        if (lastMarkdown && lastMarkdown.getAttribute('aria-busy') === 'true') return true;

        // 3. 중지 버튼 노출 확인
        const stopBtn = document.querySelector('button[aria-label*="중지"], button[aria-label*="Stop"]');
        if (stopBtn && stopBtn.offsetParent !== null && !stopBtn.disabled) return true;

        return false;
    },

    // ========== 비로그인 모드 팝업 처리 ==========

    /**
     * 비로그인 모드 사용 제한 팝업 감지 (Express 모드 제한)
     * @returns {object} { detected: boolean, title: string, hasLoginButton: boolean }
     */
    detectSignedOutDialog: function () {
        // 1. signed-out-dialog 컴포넌트 직접 확인
        const signedOutDialog = document.querySelector('signed-out-dialog');

        // 2. Material Dialog 컨테이너 확인
        const matDialog = document.querySelector('mat-dialog-container, .mat-mdc-dialog-container');

        const dialog = signedOutDialog || matDialog;
        if (!dialog) {
            return { detected: false, reason: 'no_dialog' };
        }

        // 3. 다이얼로그 제목 확인
        const title = dialog.querySelector('h1, h2, .mat-mdc-dialog-title, [mat-dialog-title]');
        const titleText = title ? title.innerText.trim() : '';

        // 4. 본문 내용 확인
        const content = dialog.querySelector('mat-dialog-content, .mat-mdc-dialog-content');
        const contentText = content ? content.innerText.trim() : '';

        // 5. 로그아웃/세션 만료 관련 키워드 매칭
        const logoutKeywords = ['로그아웃', '로그인', 'signed out', 'sign in', 'session expired', '세션'];
        const isSignedOutDialog = logoutKeywords.some(kw =>
            titleText.toLowerCase().includes(kw.toLowerCase()) ||
            contentText.toLowerCase().includes(kw.toLowerCase())
        );

        // 6. 로그인 버튼 존재 확인
        const loginBtn = dialog.querySelector('button.mat-primary, mat-dialog-actions button');

        return {
            detected: isSignedOutDialog,
            title: titleText,
            content: contentText,
            hasLoginButton: !!loginBtn,
            dialogType: signedOutDialog ? 'signed-out-dialog' : 'mat-dialog'
        };
    },

    /**
     * 비로그인 팝업 처리
     * @param {string} action - 'login', 'dismiss', 또는 'new_session'
     * @returns {object} { success: boolean, result: string }
     */
    handleSignedOutDialog: function (action) {
        const dialog = document.querySelector('signed-out-dialog') ||
            document.querySelector('mat-dialog-container, .mat-mdc-dialog-container');

        if (!dialog) {
            return { success: false, result: 'no_dialog_found' };
        }

        // 오버레이 제거 함수
        const removeOverlay = () => {
            const overlays = document.querySelectorAll('.cdk-overlay-container, .cdk-overlay-backdrop');
            overlays.forEach(el => el.remove());
        };

        if (action === 'login') {
            // 로그인 버튼 클릭하여 Google 로그인으로 이동
            const loginBtn = dialog.querySelector('button.mat-primary, mat-dialog-actions button');
            if (loginBtn) {
                this.safeClick(loginBtn);
                return { success: true, result: 'clicked_login_button' };
            }
            return { success: false, result: 'login_button_not_found' };
        }

        if (action === 'dismiss') {
            // DOM에서 강제 제거 (응답 복구는 불가)
            removeOverlay();
            dialog.remove();
            return { success: true, result: 'dialog_dismissed' };
        }

        if (action === 'new_session') {
            // 팝업 닫고 새 채팅 시작
            removeOverlay();
            dialog.remove();

            // 새 채팅 버튼 클릭 시도
            setTimeout(() => {
                const newChatBtn = document.querySelector('button[aria-label*="새 채팅"], button[aria-label*="New chat"]') ||
                    document.querySelector('a[href="/app"]');
                if (newChatBtn) this.safeClick(newChatBtn);
                else window.location.href = 'https://gemini.google.com/app';
            }, 500);

            return { success: true, result: 'starting_new_session' };
        }

        return { success: false, result: 'unknown_action' };
    },

    /**
     * 비로그인 모드 세션 복구 (팝업 발생 후 페이지 정상화)
     * @returns {Promise<object>} { success: boolean, action: string, message: string }
     */
    recoverFromSignedOut: async function () {
        const result = {
            success: false,
            action: 'none',
            message: ''
        };

        try {
            // 1. 모든 오버레이 및 다이얼로그 제거
            const overlays = document.querySelectorAll('.cdk-overlay-container, .cdk-overlay-backdrop, mat-dialog-container, signed-out-dialog');
            overlays.forEach(el => el.remove());

            // 2. body 스크롤 복구
            document.body.style.overflow = '';
            document.body.classList.remove('cdk-global-scrollblock');

            // 3. 입력창 상태 확인
            const input = this.getInputElement();
            if (!input) {
                result.action = 'needs_reload';
                result.message = '입력창을 찾을 수 없습니다. 페이지 새로고침이 필요합니다.';
                return result;
            }

            // 4. 입력창 활성화
            input.focus();

            // 5. 새 채팅으로 이동
            const newChatBtn = document.querySelector('button[aria-label*="새 채팅"], button[aria-label*="New chat"]');
            if (newChatBtn && this.isInteractable(newChatBtn)) {
                this.safeClick(newChatBtn);
                result.action = 'new_chat_started';
                result.message = '새 채팅을 시작합니다.';
            } else {
                window.location.href = 'https://gemini.google.com/app';
                result.action = 'navigated_to_new_chat';
                result.message = '새 채팅 페이지로 이동합니다.';
            }

            result.success = true;
        } catch (e) {
            result.action = 'error';
            result.message = e.message;
        }

        return result;
    },

    // ========== 로그인 권장 배너 처리 ==========

    /**
     * 로그인 권장 배너 감지 (sign-in-nudge)
     * 이미지 생성 등 기능 사용 시 나타나는 하단 배너
     * @returns {object} { detected: boolean, text: string, hasLoginButton: boolean }
     */
    detectLoginNudgeBanner: function () {
        const banner = document.querySelector('sign-in-nudge');
        if (!banner) {
            return { detected: false, reason: 'no_banner' };
        }

        const textEl = banner.querySelector('.nudge-text') || banner;
        const bannerText = textEl.innerText || '';

        const loginBtn = banner.querySelector('.sign-in-button, button.mdc-button');

        return {
            detected: true,
            bannerType: 'sign-in-nudge',
            text: bannerText,
            hasLoginButton: !!loginBtn,
            isVisible: banner.offsetParent !== null
        };
    },

    /**
     * 로그인 권장 배너 처리
     * @param {string} action - 'login', 'dismiss', 또는 'hide'
     * @returns {object} { success: boolean, result: string }
     */
    handleLoginNudgeBanner: function (action) {
        const banner = document.querySelector('sign-in-nudge');
        if (!banner) {
            return { success: false, result: 'no_banner_found' };
        }

        if (action === 'login') {
            const loginBtn = banner.querySelector('.sign-in-button, button.mdc-button');
            if (loginBtn) {
                this.safeClick(loginBtn);
                return { success: true, result: 'clicked_login_button' };
            }
            return { success: false, result: 'login_button_not_found' };
        }

        if (action === 'dismiss') {
            banner.remove();
            return { success: true, result: 'banner_removed' };
        }

        if (action === 'hide') {
            banner.style.display = 'none';
            return { success: true, result: 'banner_hidden' };
        }

        return { success: false, result: 'unknown_action' };
    },

    // ========== 통합 로그인 프롬프트 처리 ==========

    /**
     * 모든 로그인 관련 UI 요소 감지 (팝업 + 배너)
     * @returns {object} { hasAnyPrompt, signedOutDialog, loginNudgeBanner, details }
     */
    detectAllLoginPrompts: function () {
        const result = {
            hasAnyPrompt: false,
            signedOutDialog: false,
            loginNudgeBanner: false,
            loginButton: false,
            details: {}
        };

        // 1. 강제 팝업 확인
        const dialogResult = this.detectSignedOutDialog();
        if (dialogResult.detected) {
            result.signedOutDialog = true;
            result.hasAnyPrompt = true;
            result.details.dialogTitle = dialogResult.title;
        }

        // 2. 배너 확인
        const bannerResult = this.detectLoginNudgeBanner();
        if (bannerResult.detected && bannerResult.isVisible) {
            result.loginNudgeBanner = true;
            result.hasAnyPrompt = true;
            result.details.bannerText = bannerResult.text?.substring(0, 100);
        }

        // 3. 상단 로그인 버튼 확인
        const loginBtn = document.querySelector('button[aria-label*="로그인"], button[aria-label*="Sign in"]');
        if (loginBtn && loginBtn.offsetParent !== null) {
            result.loginButton = true;
        }

        return result;
    },

    /**
     * 모든 로그인 관련 UI 요소 일괄 제거
     * @returns {object} { dialogDismissed, bannerDismissed, overlayRemoved }
     */
    dismissAllLoginPrompts: function () {
        const result = {
            dialogDismissed: false,
            bannerDismissed: false,
            overlayRemoved: false
        };

        // 1. 강제 팝업 제거
        const dialog = document.querySelector('signed-out-dialog') ||
            document.querySelector('mat-dialog-container');
        if (dialog) {
            dialog.remove();
            result.dialogDismissed = true;
        }

        // 2. 오버레이 제거
        const overlays = document.querySelectorAll('.cdk-overlay-container, .cdk-overlay-backdrop');
        if (overlays.length > 0) {
            overlays.forEach(el => el.remove());
            result.overlayRemoved = true;
        }

        // 3. body 스크롤 복구
        document.body.style.overflow = '';
        document.body.classList.remove('cdk-global-scrollblock');

        // 4. 배너 제거
        const nudge = document.querySelector('sign-in-nudge');
        if (nudge) {
            nudge.remove();
            result.bannerDismissed = true;
        }

        return result;
    }
};

// 전역으로 노출
window.GeminiCommon = GeminiCommon;

// 로드 확인
console.log('%c🔧 GeminiCommon Utilities Loaded', 'background: #2196F3; color: white; font-size: 12px; padding: 4px;');
