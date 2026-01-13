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
    }
};

// 전역으로 노출
window.GeminiCommon = GeminiCommon;

// 로드 확인
console.log('%c🔧 GeminiCommon Utilities Loaded', 'background: #2196F3; color: white; font-size: 12px; padding: 4px;');
