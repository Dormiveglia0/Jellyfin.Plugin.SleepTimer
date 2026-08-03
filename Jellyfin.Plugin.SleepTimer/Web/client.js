/**
 * Sleep Timer client for Jellyfin Web 10.11.x.
 */
(function () {
    'use strict';

    if (window.__jellyfinSleepTimerLoaded) {
        return;
    }

    window.__jellyfinSleepTimerLoaded = true;

    const clientScriptSource = document.currentScript?.src || '';

    const copy = {
        zh: {
            title: '定时关闭',
            menuInactive: '未设置',
            menuHint: '暂停播放或退出视频',
            active: '剩余 {time}',
            activeMeta: '时间结束时 · {action}',
            activeTitle: '定时关闭：剩余 {time}',
            pause: '暂停播放',
            stop: '退出视频',
            actionLabel: '时间结束时',
            presets: '选择时长',
            custom: '自定义',
            minutes: '分钟',
            start: '开始计时',
            cancel: '取消定时',
            close: '关闭',
            started: '已设置 {minutes} 分钟后{action}',
            cancelled: '定时关闭已取消',
            invalid: '请输入 1 到 {max} 之间的分钟数',
            error: '操作失败，请检查 Jellyfin 服务器日志',
            expiredPause: '时间已到，播放已暂停',
            expiredStop: '时间已到，正在退出视频',
            hour: '{value} 小时',
            hourMinute: '{hours} 小时 {minutes} 分钟',
            minutePreset: '{value} 分钟'
        },
        en: {
            title: 'Sleep Timer',
            menuInactive: 'Not set',
            menuHint: 'Pause playback or exit the video',
            active: '{time} remaining',
            activeMeta: 'When time ends · {action}',
            activeTitle: 'Sleep timer: {time} remaining',
            pause: 'Pause playback',
            stop: 'Exit video',
            actionLabel: 'When time is up',
            presets: 'Choose a duration',
            custom: 'Custom',
            minutes: 'minutes',
            start: 'Start timer',
            cancel: 'Cancel timer',
            close: 'Close',
            started: 'Timer set for {minutes} minutes: {action}',
            cancelled: 'Sleep timer cancelled',
            invalid: 'Enter a value between 1 and {max} minutes',
            error: 'The operation failed. Check the Jellyfin server log.',
            expiredPause: 'Time is up. Playback has been paused.',
            expiredStop: 'Time is up. Exiting the video.',
            hour: '{value} hour',
            hourMinute: '{hours} hr {minutes} min',
            minutePreset: '{value} min'
        }
    };

    const language = String(navigator.language || '').toLowerCase().startsWith('zh')
        ? 'zh'
        : 'en';
    const messages = copy[language];

    const state = {
        options: {
            presetMinutes: [15, 30, 45, 60, 90, 120],
            defaultAction: 'pause',
            maximumMinutes: 720,
            allowCustomDuration: true
        },
        status: { isActive: false },
        selectedAction: 'pause',
        dialog: null,
        busy: false,
        failsafeTimerId: null,
        observer: null,
        ensureQueued: false,
        settingsOpenedAt: 0,
        settingsButton: null,
        pendingDialogOpen: false,
        expectedActionSheetCloseUntil: 0
    };

    const actionSheetDismissedMessage = 'ActionSheet closed without resolving';

    function text(key, values) {
        let result = messages[key] || key;
        Object.keys(values || {}).forEach(function (name) {
            result = result.replaceAll('{' + name + '}', String(values[name]));
        });
        return result;
    }

    function clientVersion() {
        try {
            return new URL(clientScriptSource, window.location.href).searchParams.get('v') || '';
        } catch {
            return '';
        }
    }

    function loadStyles() {
        const existing = document.querySelector('#sleepTimerPluginStyles');
        if (existing) {
            return Promise.resolve();
        }

        const version = clientVersion();
        const link = document.createElement('link');
        link.id = 'sleepTimerPluginStyles';
        link.rel = 'stylesheet';
        link.href = window.ApiClient.getUrl(
            'SleepTimer/client.css',
            version ? { v: version } : {});

        return new Promise(function (resolve) {
            link.addEventListener('load', resolve, { once: true });
            link.addEventListener('error', function () {
                console.warn('[Sleep Timer] Client stylesheet could not be loaded.');
                resolve();
            }, { once: true });
            document.head.appendChild(link);
        });
    }

    function apiClientReady() {
        return window.ApiClient &&
            typeof window.ApiClient.getCurrentUserId === 'function' &&
            Boolean(window.ApiClient.getCurrentUserId()) &&
            typeof window.ApiClient.ajax === 'function';
    }

    function waitForApiClient(timeoutMilliseconds) {
        const timeout = timeoutMilliseconds || 60000;
        const startedAt = Date.now();

        return new Promise(function (resolve, reject) {
            const check = function () {
                if (apiClientReady()) {
                    resolve(window.ApiClient);
                    return;
                }

                if (Date.now() - startedAt >= timeout) {
                    reject(new Error('Jellyfin ApiClient was not ready in time.'));
                    return;
                }

                window.setTimeout(check, 300);
            };

            check();
        });
    }

    function deviceId() {
        return typeof window.ApiClient.deviceId === 'function'
            ? window.ApiClient.deviceId()
            : '';
    }

    function apiRequest(path, method, payload, query) {
        const request = {
            type: method || 'GET',
            url: window.ApiClient.getUrl('SleepTimer/' + path, query || {}),
            dataType: 'json'
        };

        if (payload) {
            request.data = JSON.stringify(payload);
            request.contentType = 'application/json';
        }

        return window.ApiClient.ajax(request);
    }

    function normalizeStatus(value) {
        if (!value || !value.isActive || !value.endsAtUtc) {
            return { isActive: false };
        }

        return {
            isActive: true,
            timerId: value.timerId || null,
            durationMinutes: Number(value.durationMinutes || 0),
            endsAtUtc: value.endsAtUtc,
            action: value.action === 'stop' ? 'stop' : 'pause'
        };
    }

    async function loadOptions() {
        const options = await apiRequest('options');
        if (!options) {
            return;
        }

        const presets = Array.isArray(options.presetMinutes)
            ? options.presetMinutes.map(Number).filter(function (value) {
                return Number.isInteger(value) && value > 0;
            })
            : [];

        state.options = {
            presetMinutes: presets.length ? presets : state.options.presetMinutes,
            defaultAction: options.defaultAction === 'stop' ? 'stop' : 'pause',
            maximumMinutes: Math.max(1, Number(options.maximumMinutes) || 720),
            allowCustomDuration: options.allowCustomDuration !== false
        };
        state.selectedAction = state.options.defaultAction;
    }

    async function loadStatus() {
        if (!apiClientReady()) {
            return;
        }

        const result = await apiRequest('status', 'GET', null, { deviceId: deviceId() });
        const previousTimerId = state.status.timerId;
        state.status = normalizeStatus(result);

        if (state.status.timerId !== previousTimerId) {
            state.failsafeTimerId = null;
        }

        if (state.status.isActive) {
            state.selectedAction = state.status.action;
        }

        renderMenuEntries();
        renderDialogStatus();
    }

    async function startTimer(minutes) {
        const duration = Number(minutes);
        if (!Number.isInteger(duration) ||
            duration < 1 ||
            duration > state.options.maximumMinutes) {
            showToast(text('invalid', { max: state.options.maximumMinutes }), true);
            return;
        }

        if (state.busy) {
            return;
        }

        state.busy = true;
        updateDialogBusyState();

        try {
            const result = await apiRequest('timer', 'POST', {
                durationMinutes: duration,
                action: state.selectedAction,
                deviceId: deviceId()
            });

            state.status = normalizeStatus(result);
            state.failsafeTimerId = null;
            renderMenuEntries();
            closeDialog();
            showToast(text('started', {
                minutes: duration,
                action: actionLabel(state.selectedAction)
            }));
        } catch (error) {
            console.error('[Sleep Timer] Could not start timer:', error);
            showToast(text('error'), true);
        } finally {
            state.busy = false;
            updateDialogBusyState();
        }
    }

    async function cancelTimer() {
        if (state.busy) {
            return;
        }

        state.busy = true;
        updateDialogBusyState();

        try {
            await apiRequest('cancel', 'POST', null, { deviceId: deviceId() });
            state.status = { isActive: false };
            state.failsafeTimerId = null;
            renderMenuEntries();
            closeDialog();
            showToast(text('cancelled'));
        } catch (error) {
            console.error('[Sleep Timer] Could not cancel timer:', error);
            showToast(text('error'), true);
        } finally {
            state.busy = false;
            updateDialogBusyState();
        }
    }

    function remainingSeconds() {
        if (!state.status.isActive || !state.status.endsAtUtc) {
            return 0;
        }

        return Math.ceil((Date.parse(state.status.endsAtUtc) - Date.now()) / 1000);
    }

    function formatRemaining(seconds) {
        const safeSeconds = Math.max(0, seconds);
        const hours = Math.floor(safeSeconds / 3600);
        const minutes = Math.floor((safeSeconds % 3600) / 60);
        const secs = safeSeconds % 60;

        if (hours > 0) {
            return hours + ':' + String(minutes).padStart(2, '0') + ':' + String(secs).padStart(2, '0');
        }

        return minutes + ':' + String(secs).padStart(2, '0');
    }

    function formatPreset(minutes) {
        if (minutes < 60) {
            return text('minutePreset', { value: minutes });
        }

        const hours = Math.floor(minutes / 60);
        const remainder = minutes % 60;
        if (!remainder) {
            return text('hour', { value: hours });
        }

        return text('hourMinute', { hours: hours, minutes: remainder });
    }

    function actionLabel(action) {
        return action === 'stop' ? text('stop') : text('pause');
    }

    function isPlayerSettingsSheet(sheet) {
        const ids = new Set(
            Array.from(sheet.querySelectorAll('.actionSheetMenuItem[data-id]'))
                .map(function (item) { return item.getAttribute('data-id'); }));
        const openedFromSettingsButton =
            state.settingsOpenedAt > 0 &&
            Date.now() - state.settingsOpenedAt < 2500;
        const looksLikePlayerSettings =
            ids.has('stats') &&
            ['quality', 'aspectratio', 'playbackrate', 'repeatmode', 'suboffset']
                .some(function (id) { return ids.has(id); });

        return openedFromSettingsButton || looksLikePlayerSettings;
    }

    function createMenuEntry(sheet) {
        const scroller = sheet.querySelector('.actionSheetScroller');
        if (!scroller || sheet.querySelector('.sleepTimerPluginMenuItem')) {
            return false;
        }

        const button = document.createElement('button');
        button.setAttribute('type', 'button');
        button.setAttribute('aria-haspopup', 'dialog');
        button.className =
            'listItem listItem-button actionSheetMenuItem ' +
            'sleepTimerPluginMenuItem emby-button';
        button.innerHTML = [
            '<div class="listItemBody actionsheetListItemBody">',
            '  <div class="listItemBodyText actionSheetItemText">' + text('title') + '</div>',
            '</div>',
            '<div class="listItemAside actionSheetItemAsideText sleepTimerPluginMenuAside"></div>'
        ].join('');
        button.addEventListener('click', function (event) {
            // Keep Jellyfin's exact row styling, but stop its delegated click
            // handler so an unknown item id is never sent to the player menu.
            event.preventDefault();
            event.stopPropagation();
            openDialogFromSettings(button);
        });

        const qualityItem = scroller.querySelector(
            '.actionSheetMenuItem[data-id="quality"]');
        const statsItem = scroller.querySelector(
            '.actionSheetMenuItem[data-id="stats"]');
        const referenceItem = qualityItem || statsItem ||
            scroller.querySelector('.actionSheetMenuItem');
        ['listItem-border', 'listItem-focusscale', 'actionsheet-xlargeFont']
            .forEach(function (className) {
                if (referenceItem?.classList.contains(className)) {
                    button.classList.add(className);
                }
            });
        if (qualityItem) {
            qualityItem.insertAdjacentElement('afterend', button);
        } else if (statsItem) {
            statsItem.insertAdjacentElement('beforebegin', button);
        } else {
            scroller.appendChild(button);
        }

        return true;
    }

    function ensureSettingsMenuEntries() {
        let injectedFromPendingClick = false;
        document.querySelectorAll('.actionSheet').forEach(function (sheet) {
            if (!isPlayerSettingsSheet(sheet)) {
                return;
            }

            if (createMenuEntry(sheet)) {
                injectedFromPendingClick = state.settingsOpenedAt > 0;
            }
        });

        if (injectedFromPendingClick) {
            state.settingsOpenedAt = 0;
        }
        renderMenuEntries();
    }

    function renderMenuEntries() {
        const seconds = remainingSeconds();
        const active = state.status.isActive && seconds > 0;
        document.querySelectorAll('.sleepTimerPluginMenuItem').forEach(function (button) {
            const aside = button.querySelector('.sleepTimerPluginMenuAside');
            const countdown = active ? formatRemaining(seconds) : text('menuInactive');
            const ariaDetail = active
                ? text('activeMeta', { action: actionLabel(state.status.action) })
                : text('menuHint');
            const ariaLabel =
                text('title') + ', ' + countdown + ', ' + ariaDetail;

            button.classList.toggle('is-active', active);
            aside.classList.toggle('buttonActive', active);
            if (aside.textContent !== countdown) {
                aside.textContent = countdown;
            }
            if (button.getAttribute('aria-label') !== ariaLabel) {
                button.setAttribute('aria-label', ariaLabel);
            }
        });
    }

    function ignoreExpectedActionSheetDismissal(event) {
        const reason = event.reason;
        const message = reason && typeof reason.message === 'string'
            ? reason.message
            : String(reason || '');
        if (Date.now() <= state.expectedActionSheetCloseUntil &&
            message === actionSheetDismissedMessage) {
            state.expectedActionSheetCloseUntil = 0;
            event.preventDefault();
        }
    }

    function actionSheetIsVisible(sheet) {
        if (!sheet || !sheet.isConnected || sheet.classList.contains('hide')) {
            return false;
        }

        const style = window.getComputedStyle(sheet);
        return style.display !== 'none' &&
            style.visibility !== 'hidden' &&
            sheet.getClientRects().length > 0;
    }

    function openDialogFromSettings(button) {
        if (state.pendingDialogOpen) {
            return;
        }

        const sheet = button.closest('.actionSheet');
        const returnFocus = state.settingsButton instanceof HTMLElement &&
            document.contains(state.settingsButton)
            ? state.settingsButton
            : null;
        if (!actionSheetIsVisible(sheet)) {
            showDialog(returnFocus);
            return;
        }

        state.pendingDialogOpen = true;
        state.expectedActionSheetCloseUntil = Date.now() + 4000;
        let completed = false;
        let fallbackTimerId = null;
        let closeCheckFrameId = null;
        const sheetWasInert = sheet.hasAttribute('inert');

        const activeElement = document.activeElement;
        if (sheet.contains(activeElement) && typeof activeElement.blur === 'function') {
            activeElement.blur();
        }
        sheet.setAttribute('inert', '');

        const restoreSheetInteractivity = function () {
            if (!sheetWasInert && sheet.isConnected) {
                sheet.removeAttribute('inert');
            }
        };
        const cleanup = function () {
            if (fallbackTimerId) {
                window.clearTimeout(fallbackTimerId);
                fallbackTimerId = null;
            }
            if (closeCheckFrameId) {
                window.cancelAnimationFrame(closeCheckFrameId);
                closeCheckFrameId = null;
            }
            sheet.removeEventListener('close', openAfterClose);
        };
        const failToOpen = function () {
            if (completed) {
                return;
            }

            completed = true;
            state.pendingDialogOpen = false;
            state.expectedActionSheetCloseUntil = 0;
            cleanup();
            restoreSheetInteractivity();
            console.warn('[Sleep Timer] Player settings menu did not close in time.');
            showToast(text('error'), true);
        };
        const revealDialog = function () {
            if (completed || actionSheetIsVisible(sheet)) {
                return;
            }

            completed = true;
            state.pendingDialogOpen = false;
            cleanup();
            restoreSheetInteractivity();
            window.requestAnimationFrame(function () {
                const hasOpenSheet = Array.from(document.querySelectorAll('.actionSheet'))
                    .some(actionSheetIsVisible);
                if (hasOpenSheet) {
                    state.expectedActionSheetCloseUntil = 0;
                    console.warn('[Sleep Timer] Another action sheet is still open.');
                    showToast(text('error'), true);
                    return;
                }

                showDialog(returnFocus);
            });
        };
        const openAfterClose = function () {
            revealDialog();
        };
        const checkForClose = function () {
            if (completed) {
                return;
            }
            if (!actionSheetIsVisible(sheet)) {
                revealDialog();
                return;
            }

            closeCheckFrameId = window.requestAnimationFrame(checkForClose);
        };

        sheet.addEventListener('close', openAfterClose, { once: true });
        fallbackTimerId = window.setTimeout(failToOpen, 1800);
        closeCheckFrameId = window.requestAnimationFrame(checkForClose);
        try {
            window.history.back();
        } catch (error) {
            console.warn('[Sleep Timer] Could not close player settings:', error);
            failToOpen();
        }
    }

    function markPlayerSettingsOpen(event) {
        const target = event.target instanceof Element
            ? event.target.closest('.btnVideoOsdSettings')
            : null;
        if (!target) {
            return;
        }

        state.settingsOpenedAt = Date.now();
        state.settingsButton = target;
        window.setTimeout(scheduleEnsureSettingsMenuEntries, 0);
        window.setTimeout(scheduleEnsureSettingsMenuEntries, 120);
    }

    function showDialog(returnFocus) {
        closeDialog();

        const previousFocus = returnFocus instanceof HTMLElement
            ? returnFocus
            : document.activeElement instanceof HTMLElement
            ? document.activeElement
            : null;
        const overlay = document.createElement('div');
        overlay.className = 'sleepTimerPluginOverlay';
        overlay.innerHTML = [
            '<section class="sleepTimerPluginDialog dialog" role="dialog" aria-modal="true" aria-labelledby="sleepTimerPluginTitle" tabindex="-1">',
            '  <header class="sleepTimerPluginHeader">',
            '    <div class="sleepTimerPluginTitleGroup">',
            '      <span class="material-icons sleepTimerPluginMoon buttonActive" aria-hidden="true">bedtime</span>',
            '      <h2 id="sleepTimerPluginTitle">' + text('title') + '</h2>',
            '    </div>',
            '    <button type="button" class="sleepTimerPluginClose paper-icon-button-light" aria-label="' + text('close') + '">',
            '      <span class="material-icons" aria-hidden="true">close</span>',
            '    </button>',
            '  </header>',
            '  <div class="sleepTimerPluginStatus" aria-live="polite">',
            '    <div class="sleepTimerPluginStatusTime"></div>',
            '    <div class="sleepTimerPluginStatusMeta secondaryText"></div>',
            '  </div>',
            '  <div class="sleepTimerPluginSection">',
            '    <div class="sleepTimerPluginLabel">' + text('actionLabel') + '</div>',
            '    <div class="sleepTimerPluginActions" role="group" aria-label="' + text('actionLabel') + '">',
            '      <button type="button" class="sleepTimerPluginChoice emby-button raised" data-action="pause"><span class="material-icons" aria-hidden="true">pause</span><span>' + text('pause') + '</span></button>',
            '      <button type="button" class="sleepTimerPluginChoice emby-button raised" data-action="stop"><span class="material-icons" aria-hidden="true">stop</span><span>' + text('stop') + '</span></button>',
            '    </div>',
            '  </div>',
            '  <div class="sleepTimerPluginSection">',
            '    <div class="sleepTimerPluginLabel">' + text('presets') + '</div>',
            '    <div class="sleepTimerPluginPresets"></div>',
            '  </div>',
            '  <div class="sleepTimerPluginCustom"></div>',
            '  <button type="button" class="sleepTimerPluginCancel emby-button button-flat secondaryText">' + text('cancel') + '</button>',
            '</section>'
        ].join('');

        document.body.appendChild(overlay);

        const closeButton = overlay.querySelector('.sleepTimerPluginClose');
        closeButton.addEventListener('click', closeDialog);
        overlay.addEventListener('click', function (event) {
            if (event.target === overlay) {
                closeDialog();
            }
        });

        const actionButtons = Array.from(overlay.querySelectorAll('[data-action]'));
        actionButtons.forEach(function (button) {
            button.addEventListener('click', function () {
                state.selectedAction = button.dataset.action === 'stop' ? 'stop' : 'pause';
                renderActionButtons();
            });
        });

        const presetsContainer = overlay.querySelector('.sleepTimerPluginPresets');
        state.options.presetMinutes.forEach(function (minutes) {
            const preset = document.createElement('button');
            preset.type = 'button';
            preset.className = 'sleepTimerPluginPreset emby-button raised';
            preset.textContent = formatPreset(minutes);
            preset.addEventListener('click', function () {
                startTimer(minutes);
            });
            presetsContainer.appendChild(preset);
        });

        const customContainer = overlay.querySelector('.sleepTimerPluginCustom');
        if (state.options.allowCustomDuration) {
            const maximumLength = String(state.options.maximumMinutes).length;
            const initialMinutes = Math.min(30, state.options.maximumMinutes);
            customContainer.innerHTML = [
                '<label class="sleepTimerPluginLabel" id="sleepTimerPluginCustomLabel" for="sleepTimerPluginMinutes">' + text('custom') + '</label>',
                '<div class="sleepTimerPluginCustomRow">',
                '  <div class="sleepTimerPluginInputShell">',
                '    <input id="sleepTimerPluginMinutes" class="sleepTimerPluginMinutes emby-input" type="text" inputmode="numeric" pattern="[0-9]*" maxlength="' + maximumLength + '" value="' + initialMinutes + '" autocomplete="off" autocapitalize="off" enterkeyhint="done" spellcheck="false" aria-describedby="sleepTimerPluginMinutesUnit">',
                '    <span id="sleepTimerPluginMinutesUnit" class="sleepTimerPluginInputSuffix secondaryText">' + text('minutes') + '</span>',
                '  </div>',
                '  <button type="button" class="sleepTimerPluginStart emby-button raised button-submit">' + text('start') + '</button>',
                '</div>',
            ].join('');

            customContainer.querySelector('.sleepTimerPluginStart').addEventListener('click', function () {
                startTimer(Number(customContainer.querySelector('.sleepTimerPluginMinutes').value));
            });
            const minutesInput = customContainer.querySelector('.sleepTimerPluginMinutes');
            minutesInput.addEventListener('input', function (event) {
                if (event.isComposing) {
                    return;
                }

                const digits = event.target.value.replace(/\D/g, '').slice(0, maximumLength);
                if (event.target.value !== digits) {
                    event.target.value = digits;
                }
            });
            minutesInput.addEventListener('keydown', function (event) {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    startTimer(Number(event.target.value));
                }
            });
        }

        overlay.querySelector('.sleepTimerPluginCancel').addEventListener('click', cancelTimer);

        const keyHandler = function (event) {
            if (event.key === 'Escape') {
                event.preventDefault();
                closeDialog();
                return;
            }

            if (event.key !== 'Tab') {
                return;
            }

            const focusable = Array.from(
                overlay.querySelectorAll('button:not(:disabled):not([hidden]), input:not(:disabled)'))
                .filter(function (element) {
                    return element.getClientRects().length > 0;
                });
            if (!focusable.length) {
                return;
            }

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };
        const focusInHandler = function (event) {
            if (state.dialog?.overlay === overlay && !overlay.contains(event.target)) {
                event.stopPropagation();
                closeButton.focus({ preventScroll: true });
            }
        };
        document.addEventListener('keydown', keyHandler);
        document.addEventListener('focusin', focusInHandler, true);
        state.dialog = {
            overlay: overlay,
            keyHandler: keyHandler,
            focusInHandler: focusInHandler,
            previousFocus: previousFocus
        };

        renderActionButtons();
        renderDialogStatus();
        updateDialogBusyState();
        closeButton.focus({ preventScroll: true });

        loadStatus().catch(function (error) {
            console.warn('[Sleep Timer] Could not refresh status for dialog:', error);
        });
    }

    function closeDialog() {
        if (!state.dialog) {
            return;
        }

        const previousFocus = state.dialog.previousFocus;
        document.removeEventListener('keydown', state.dialog.keyHandler);
        document.removeEventListener('focusin', state.dialog.focusInHandler, true);
        state.dialog.overlay.remove();
        state.dialog = null;
        if (previousFocus && document.contains(previousFocus)) {
            previousFocus.focus({ preventScroll: true });
        }
    }

    function renderActionButtons() {
        if (!state.dialog) {
            return;
        }

        state.dialog.overlay.querySelectorAll('[data-action]').forEach(function (button) {
            const selected = button.dataset.action === state.selectedAction;
            button.classList.toggle('is-selected', selected);
            button.classList.toggle('button-submit', selected);
            button.setAttribute('aria-pressed', String(selected));
        });
    }

    function renderDialogStatus() {
        if (!state.dialog) {
            return;
        }

        const status = state.dialog.overlay.querySelector('.sleepTimerPluginStatus');
        const statusTime = status.querySelector('.sleepTimerPluginStatusTime');
        const statusMeta = status.querySelector('.sleepTimerPluginStatusMeta');
        const cancel = state.dialog.overlay.querySelector('.sleepTimerPluginCancel');
        if (state.status.isActive) {
            status.hidden = false;
            status.classList.add('is-active');
            statusTime.classList.add('buttonActive');
            statusTime.textContent = text('active', {
                time: formatRemaining(remainingSeconds())
            });
            statusMeta.hidden = false;
            statusMeta.textContent = text('activeMeta', {
                action: actionLabel(state.status.action)
            });
            cancel.hidden = false;
        } else {
            status.hidden = true;
            status.classList.remove('is-active');
            statusTime.classList.remove('buttonActive');
            statusTime.textContent = '';
            statusMeta.hidden = true;
            statusMeta.textContent = '';
            cancel.hidden = true;
        }
    }

    function updateDialogBusyState() {
        if (!state.dialog) {
            return;
        }

        state.dialog.overlay.querySelectorAll('button, input').forEach(function (element) {
            element.disabled = state.busy;
        });
    }

    function runClientFailsafe() {
        if (!state.status.isActive ||
            remainingSeconds() > 0 ||
            state.failsafeTimerId === state.status.timerId) {
            return;
        }

        state.failsafeTimerId = state.status.timerId;
        const video = document.querySelector('video');

        if (state.status.action === 'stop') {
            if (video && !video.paused) {
                video.pause();
            }
            showToast(text('expiredStop'));
            window.setTimeout(function () {
                if (document.querySelector('#videoOsdPage') &&
                    String(window.location.hash).startsWith('#/video')) {
                    window.history.back();
                }
            }, 150);
            return;
        }

        if (video && !video.paused) {
            video.pause();
        } else if (!video) {
            const pauseButton = document.querySelector('#videoOsdPage .btnPause');
            if (pauseButton) {
                pauseButton.click();
            }
        }
        showToast(text('expiredPause'));
    }

    function tick() {
        renderMenuEntries();
        if (state.status.isActive) {
            renderDialogStatus();
            runClientFailsafe();
        }
    }

    function showToast(message, isError) {
        const existing = document.querySelector('.sleepTimerPluginToast');
        if (existing) {
            existing.remove();
        }

        const toast = document.createElement('div');
        toast.className = 'sleepTimerPluginToast toast' + (isError ? ' button-delete' : '');
        toast.setAttribute('role', 'status');
        toast.textContent = message;
        document.body.appendChild(toast);
        window.requestAnimationFrame(function () {
            toast.classList.add('is-visible');
        });
        window.setTimeout(function () {
            toast.classList.remove('is-visible');
            window.setTimeout(function () { toast.remove(); }, 220);
        }, 3200);
    }

    function scheduleEnsureSettingsMenuEntries() {
        if (state.ensureQueued) {
            return;
        }

        state.ensureQueued = true;
        window.requestAnimationFrame(function () {
            state.ensureQueued = false;
            ensureSettingsMenuEntries();
        });
    }

    async function initialize() {
        try {
            await waitForApiClient();
            await loadStyles();
            await Promise.allSettled([loadOptions(), loadStatus()]);
            ensureSettingsMenuEntries();

            state.observer = new MutationObserver(scheduleEnsureSettingsMenuEntries);
            state.observer.observe(document.body, { childList: true, subtree: true });
            document.addEventListener('click', markPlayerSettingsOpen, true);
            window.addEventListener('unhandledrejection', ignoreExpectedActionSheetDismissal);
            window.addEventListener('hashchange', scheduleEnsureSettingsMenuEntries);
            window.setInterval(tick, 1000);
            window.setInterval(function () {
                loadStatus().catch(function (error) {
                    console.debug('[Sleep Timer] Status refresh failed:', error);
                });
            }, 5000);

            window.JellyfinSleepTimer = {
                version: clientVersion() || '1.3.1.0',
                open: showDialog,
                refresh: loadStatus,
                cancel: cancelTimer,
                diagnostics: function () {
                    return {
                        version: clientVersion() || '1.3.1.0',
                        apiReady: apiClientReady(),
                        menuEntries: document.querySelectorAll(
                            '.sleepTimerPluginMenuItem').length,
                        status: state.status
                    };
                }
            };
            console.info(
                '[Sleep Timer] Client initialized. Open the player settings menu to use it.');
        } catch (error) {
            console.error('[Sleep Timer] Client initialization failed:', error);
        }
    }

    initialize();
}());
