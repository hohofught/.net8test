/**
 * Browser Mode Automation
 * 
 * 브라우저 모드 번역 전용 스크립트입니다.
 * GeminiCommon.js에 의존합니다.
 * PuppeteerGeminiAutomation.cs에서 사용됩니다.
 */

const BrowserModeAutomation = {
    // GeminiCommon 참조 (편의용)
    get common() {
        return window.GeminiCommon;
    },

    // ========== 입력 관련 ==========

    /**
     * 입력창 준비 여부 확인
     * @returns {boolean}
     */
    checkInputReady: function () {
        return !!(document.querySelector('.ql-editor') ||
            document.querySelector('div[contenteditable="true"]'));
    },

    /**
     * 입력창에 포커스를 주고 기존 내용을 비웁니다.
     * @returns {boolean}
     */
    focusAndClear: function () {
        const input = this.common.getInputElement();
        if (!input) return false;
        input.focus();
        // TrustedHTML 정책 우회: DOM API로 내용 삭제
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
        return true;
    },

    /**
     * 입력창에 텍스트 입력 (React 상태 동기화 포함)
     * @param {string} text - 입력할 텍스트
     * @returns {boolean}
     */
    writeText: function (text) {
        const input = this.common.getInputElement();
        if (!input) return false;

        input.focus();
        document.execCommand('selectAll', false, null);
        document.execCommand('delete', false, null);
        document.execCommand('insertText', false, text);

        // React/Angular 상태 업데이트 유도
        ['input', 'change', 'blur'].forEach(name => {
            input.dispatchEvent(new Event(name, { bubbles: true }));
        });

        return true;
    },

    // ========== 전송 관련 ==========

    /**
     * 전송 버튼 클릭
     * @returns {string} 'clicked' | 'not_found'
     */
    clickSendButton: function () {
        const btn = document.querySelector('.send-button:not(.stop)') ||
            document.querySelector('button[aria-label="보내기"]') ||
            document.querySelector('button[aria-label="Send message"]');
        if (btn && !btn.disabled) {
            btn.click();
            return 'clicked';
        }
        return 'not_found';
    },

    // ========== 응답 관련 ==========

    /**
     * 최신 응답 텍스트 추출 (이미지 응답 필터링 포함)
     * @returns {string}
     */
    getResponse: function () {
        const responses = document.querySelectorAll('message-content.model-response-text, .model-response-text');
        if (responses.length === 0) return '';

        const lastResponse = responses[responses.length - 1];

        // 1. 마크다운 영역에서 텍스트 추출 시도
        const markdownEl = lastResponse.querySelector('.markdown');
        if (markdownEl) {
            const text = markdownEl.innerText || '';
            // 이미지 생성 관련 메타 텍스트 필터링
            const cleaned = text.trim()
                .replace(/^image_generated\s*/gi, '')
                .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                .replace(/^\[Image[^\]]*\]\s*/gi, '');
            if (cleaned.length > 0) return cleaned;
        }

        // 2. 코드 블록 영역 확인
        const codeBlocks = lastResponse.querySelectorAll('code-block, pre code');
        if (codeBlocks.length > 0) {
            let codeText = '';
            codeBlocks.forEach(block => {
                const code = block.innerText || block.textContent || '';
                codeText += code + '\n';
            });
            if (codeText.trim().length > 0) return codeText.trim();
        }

        // 3. 일반 텍스트 추출 (이미지 버튼 영역 제외)
        let text = '';
        const walker = document.createTreeWalker(lastResponse, NodeFilter.SHOW_TEXT, {
            acceptNode: function (node) {
                // 이미지 버튼이나 이미지 관련 요소 내부 텍스트 제외
                const parent = node.parentElement;
                if (parent && (
                    parent.closest('button.image-button') ||
                    parent.closest('.image-container') ||
                    parent.closest('[data-image]') ||
                    parent.closest('.generated-image') ||
                    parent.tagName === 'BUTTON'
                )) {
                    return NodeFilter.FILTER_REJECT;
                }
                return NodeFilter.FILTER_ACCEPT;
            }
        });

        let node;
        while (node = walker.nextNode()) {
            const nodeText = node.textContent.trim();
            if (nodeText.length > 0) {
                text += nodeText + ' ';
            }
        }

        text = text.trim()
            .replace(/^image_generated\s*/gi, '')
            .replace(/^\[이미지[^\]]*\]\s*/gi, '')
            .replace(/^\[Image[^\]]*\]\s*/gi, '');

        // 4. 최종 fallback: innerText 직접 사용
        if (text.length === 0) {
            text = (lastResponse.innerText || '').trim()
                .replace(/^image_generated\s*/gi, '')
                .replace(/^\[이미지[^\]]*\]\s*/gi, '')
                .replace(/^\[Image[^\]]*\]\s*/gi, '');
        }

        return text;
    },

    /**
     * 응답 개수 확인
     * @returns {number}
     */
    getResponseCount: function () {
        return document.querySelectorAll('message-content.model-response-text, .model-response-text').length;
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

    /**
     * 다음 입력 대기 상태 확인
     * @returns {boolean}
     */
    isReadyForNextInput: function () {
        const input = document.querySelector('.ql-editor, div[contenteditable="true"]');
        if (!input || input.getAttribute('contenteditable') !== 'true') return false;

        const sendBtn = document.querySelector('.send-button');
        if (sendBtn && sendBtn.classList.contains('stop')) return false;

        return input.textContent.trim() === '' || input.classList.contains('ql-blank');
    },

    // ========== 로그인/오류 관련 ==========

    /**
     * 로그인 필요 여부 확인
     * @returns {string} 'login_needed' | 'ok'
     */
    checkLogin: function () {
        return document.querySelector('button[aria-label*="Sign in"], button[aria-label*="로그인"]')
            ? 'login_needed'
            : 'ok';
    },

    /**
     * 로그인 상태 정밀 진단
     * @returns {string} 'logged_out' | 'logged_in'
     */
    diagnoseLogin: function () {
        const loginBtn = document.querySelector('button[aria-label*="로그인"], button[aria-label*="Sign in"]');
        return loginBtn && loginBtn.offsetParent !== null ? 'logged_out' : 'logged_in';
    },

    /**
     * 오류 메시지 수집
     * @returns {string}
     */
    diagnoseError: function () {
        // 1. 하단 스낵바
        const snackbar = document.querySelector('m-snackbar, snack-bar, .snackbar, .cdk-overlay-container .error');
        if (snackbar && snackbar.offsetParent !== null && snackbar.innerText.trim().length > 0) {
            return snackbar.innerText.trim().substring(0, 100);
        }

        // 2. 알림 영역
        const alert = document.querySelector('[role="alert"], .simple-message.error');
        if (alert && alert.offsetParent !== null && alert.innerText.trim().length > 0) {
            return alert.innerText.trim().substring(0, 100);
        }

        // 3. 브라우저 차단/연결 오류 등
        const err = document.querySelector('[class*="error"]');
        if (err && err.offsetParent !== null && err.innerText.length > 5) {
            const txt = err.innerText.trim();
            if (txt.includes('문제가 발생') || txt.includes('Something went wrong') || txt.includes('error')) {
                return txt.substring(0, 100);
            }
        }

        return '';
    },

    /**
     * 오류 상태 복구 시도
     * @returns {string}
     */
    recoverFromError: function () {
        const containers = ['m-snackbar', 'snack-bar', '.snackbar', '[role="alert"]'];
        for (const sel of containers) {
            const el = document.querySelector(sel);
            if (el && el.offsetParent !== null) {
                const btn = el.querySelector('button');
                if (btn && btn.offsetParent !== null) {
                    btn.click();
                    return 'clicked_recovery_button';
                }
            }
        }

        // 스낵바 자체를 클릭하여 닫기
        const snackbar = document.querySelector('m-snackbar, snack-bar');
        if (snackbar && snackbar.offsetParent !== null) {
            snackbar.click();
            return 'clicked_snackbar_to_dismiss';
        }

        return 'no_action_taken';
    },

    // ========== 응답 중지 ==========

    /**
     * Gemini 응답 생성 중지
     * @returns {string}
     */
    stopGeminiResponse: function () {
        // 1. Send 버튼이 Stop 상태인 경우
        const sendBtn = document.querySelector('.send-button.stop');
        if (sendBtn && sendBtn.offsetParent !== null && !sendBtn.disabled) {
            sendBtn.click();
            return 'stopped_via_send_button';
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
                btn.click();
                return 'stopped_via_' + sel;
            }
        }

        // 3. mat-icon으로 stop 검색
        const icons = document.querySelectorAll('mat-icon');
        for (const icon of icons) {
            if (icon.textContent === 'stop' || icon.textContent === 'stop_circle') {
                const parentBtn = icon.closest('button');
                if (parentBtn && parentBtn.offsetParent !== null && !parentBtn.disabled) {
                    parentBtn.click();
                    return 'stopped_via_mat_icon';
                }
            }
        }

        return 'no_stop_button_found';
    },

    // ========== 모델 선택 ==========

    /**
     * 모델 전환 (flash/pro)
     * @param {string} targetModel - 'flash' 또는 'pro'
     * @returns {Promise<string>}
     */
    selectModel: async function (targetModel) {
        const common = this.common;

        // 유틸리티 함수
        const isInteractable = common.isInteractable.bind(common);
        const safeClick = common.safeClick.bind(common);
        const delay = common.delay.bind(common);

        // 1. 현재 모드 확인
        const modeBtn = document.querySelector('button.input-area-switch') ||
            document.querySelector('button[aria-haspopup="true"][aria-label*="모델"]') ||
            document.querySelector('button[aria-haspopup="true"]');

        if (!modeBtn || !isInteractable(modeBtn)) {
            return 'picker_not_found';
        }

        const currentText = modeBtn.innerText.toLowerCase();
        if (targetModel === 'flash' && (currentText.includes('flash') || currentText.includes('빠른'))) {
            return 'already_selected_flash';
        }
        if (targetModel === 'pro' && currentText.includes('pro') && !currentText.includes('flash')) {
            return 'already_selected_pro';
        }

        // 2. 메뉴 열기
        safeClick(modeBtn);
        await delay(600);

        // 3. 메뉴 항목 선택
        const menuSelectors = [
            'button[role="menuitemradio"]',
            'button.mat-mdc-menu-item',
            '.mat-mdc-menu-content button',
            'button.bard-mode-list-button',
            '[role="menuitem"]',
            'mat-list-item'
        ];

        let menuItems = [];
        for (const sel of menuSelectors) {
            const found = document.querySelectorAll(sel);
            if (found.length > 0) {
                menuItems = Array.from(found);
                break;
            }
        }

        // 4. 대상 모델 항목 찾기
        let targetItem = null;
        for (const item of menuItems) {
            const itemText = item.innerText.toLowerCase();
            if (targetModel === 'flash' && (itemText.includes('flash') || itemText.includes('빠른'))) {
                targetItem = item;
                break;
            }
            if (targetModel === 'pro' && itemText.includes('pro') && !itemText.includes('flash')) {
                targetItem = item;
                break;
            }
        }

        if (targetItem && isInteractable(targetItem)) {
            safeClick(targetItem);
            await delay(500);
            return 'switched_to_' + targetModel;
        }

        // 메뉴 닫기 (ESC)
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        return 'item_not_found_for_' + targetModel;
    }
};

// 전역으로 노출
window.BrowserModeAutomation = BrowserModeAutomation;

// 로드 확인
console.log('%c🌐 BrowserModeAutomation Loaded', 'background: #4CAF50; color: white; font-size: 12px; padding: 4px;');
