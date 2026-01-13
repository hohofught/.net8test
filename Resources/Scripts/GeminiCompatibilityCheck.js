/**
 * Gemini Automation Compatibility Checker
 * 
 * 이 스크립트를 Gemini 웹페이지(https://gemini.google.com/app)의 개발자 도구(F12) 콘솔에 붙여넣어 실행하세요.
 * C# 코드에서 사용하는 선택자(Selector)들이 현재 페이지에서 유효한지 확인합니다.
 */

(function () {
    console.clear();
    console.log('%c Gemini Automation Check Started ', 'background: #222; color: #bada55; font-size: 14px; padding: 4px;');

    const results = {
        input: false,
        sendButton: false,
        response: false,
        generating: false
    };

    // 1. 입력창 확인 (Input Field)
    console.group('1. Input Field Check');
    const inputSelectors = [
        '.ql-editor',
        'div[contenteditable="true"]'
    ];

    let inputFound = null;
    inputSelectors.forEach(sel => {
        const el = document.querySelector(sel);
        console.log(`Selector "${sel}":`, el ? '%cFOUND' : '%cNOT FOUND', el ? 'color: green' : 'color: red');
        if (el && !inputFound) inputFound = el;
    });

    if (inputFound) {
        results.input = true;
        console.log('%c[OK] Input field detected.', 'color: green; font-weight: bold;');

        // execCommand 호환성 체크 (deprecated but useful)
        try {
            const supported = document.queryCommandSupported('insertText');
            console.log(`document.execCommand('insertText') supported: ${supported}`);
            if (!supported) console.warn('Warning: execCommand("insertText") might not work!');
        } catch (e) {
            console.error('Error checking execCommand:', e);
        }
    } else {
        console.error('%c[FAIL] No input field found!', 'color: red; font-weight: bold;');
    }
    console.groupEnd();

    // 2. 전송 버튼 확인 (Send Button)
    console.group('2. Send Button Check');
    // C# 코드의 로직 순서대로 체크
    let sendBtn = document.querySelector('button.send-button');
    console.log('Strategy 1 (button.send-button):', sendBtn ? '%cFOUND' : '%cNOT FOUND', sendBtn ? 'color: green' : 'color: orange');

    if (!sendBtn) {
        const ariaLabels = ['보내기', 'Send message', '전송'];
        const ariaSelector = ariaLabels.map(l => `button[aria-label="${l}"]`).join(', ');
        sendBtn = document.querySelector(ariaSelector);
        console.log(`Strategy 2 (aria-label="..."):`, sendBtn ? '%cFOUND' : '%cNOT FOUND', sendBtn ? 'color: green' : 'color: orange');
    }

    if (!sendBtn) {
        const icons = Array.from(document.querySelectorAll('mat-icon'));
        const sendIcon = icons.find(icon => icon.textContent.trim() === 'send');
        if (sendIcon) {
            sendBtn = sendIcon.closest('button');
            console.log('Strategy 3 (mat-icon "send"):', sendBtn ? '%cFOUND' : '%cNOT FOUND', sendBtn ? 'color: green' : 'color: orange');
        } else {
            console.log('Strategy 3 (mat-icon "send"): %cNOT FOUND', 'color: orange');
        }
    }

    if (sendBtn) {
        results.sendButton = true;
        console.log('%c[OK] Send button detected.', 'color: green; font-weight: bold;', sendBtn);
    } else {
        console.error('%c[FAIL] Send button NOT found! Logic needs update.', 'color: red; font-weight: bold;');
    }
    console.groupEnd();

    // 3. 응답 요소 확인 (Response Elements)
    console.group('3. Response Elements Check');
    const responseSelectors = [
        'message-content.model-response-text',
        '.model-response-text',
        'div[data-message-author-role="model"]',
        '.response-container .markdown-content',
        '.conversation-turn .model-response',
        'model-response message-content'
    ];

    let responseFound = false;
    responseSelectors.forEach(sel => {
        const els = document.querySelectorAll(sel);
        if (els.length > 0) {
            console.log(`Selector "${sel}": %cFOUND (${els.length} elements)`, 'color: green');
            responseFound = true;
        } else {
            console.log(`Selector "${sel}": %cNOT FOUND`, 'color: gray');
        }
    });

    // Fallback check
    if (!responseFound) {
        console.log('Checking fallback (conversation-turn)...');
        const turns = document.querySelectorAll('.conversation-turn, [data-turn-id]');
        if (turns.length > 0) console.log(`Fallback: Found ${turns.length} conversation turns.`);
        else console.log('Fallback: No conversation turns found (maybe empty chat?).');
    }

    if (responseFound) {
        results.response = true;
        console.log('%c[OK] Response structure matches.', 'color: green; font-weight: bold;');
    } else {
        console.warn('%c[WARN] No standard response elements found. (Normal if chat is empty)', 'color: orange; font-weight: bold;');
    }
    console.groupEnd();

    // Final Report
    console.log('%c--- COMPATIBILITY REPORT ---', 'font-size: 14px; font-weight: bold;');
    if (results.input && results.sendButton) {
        console.log('%cPASS: Basic interaction (Input + Send) is possible.', 'color: green; font-size: 16px; font-weight: bold;');
    } else {
        console.log('%cFAIL: Critical elements missing. Code needs refactoring.', 'color: red; font-size: 16px; font-weight: bold;');
    }
})();
