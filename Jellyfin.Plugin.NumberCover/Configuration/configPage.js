const pluginId = 'c9c7b9d7-8eab-4f1b-8bf4-5ddc3f5ae9a7';
const defaults = {
    EnableJableSource: false,
    OnlyFillMissing: true,
    MaxRequestsPerMinute: 12,
    TimeoutSeconds: 10
};

function withDefaults(config) {
    return Object.assign({}, defaults, config || {});
}

function integerOrDefault(value, fallback) {
    const integer = Number.parseInt(value, 10);
    return Number.isFinite(integer) ? integer : fallback;
}

export default function NumberCoverConfigController(page) {
    const apiClient = window.ApiClient;
    const dashboard = window.Dashboard;
    const form = page.querySelector('#NumberCoverConfigForm');
    const errorMessage = page.querySelector('#NumberCoverConfigError');
    const enableJableSource = page.querySelector('#EnableJableSource');
    const onlyFillMissing = page.querySelector('#OnlyFillMissing');
    const maxRequestsPerMinute = page.querySelector('#MaxRequestsPerMinute');
    const timeoutSeconds = page.querySelector('#TimeoutSeconds');

    function showError(message) {
        errorMessage.textContent = message;
        errorMessage.style.display = 'block';
    }

    function clearError() {
        errorMessage.textContent = '';
        errorMessage.style.display = 'none';
    }

    async function loadConfiguration() {
        dashboard.showLoadingMsg();
        try {
            let config = await apiClient.getPluginConfiguration(pluginId);
            config = withDefaults(config);
            enableJableSource.checked = config.EnableJableSource;
            onlyFillMissing.checked = config.OnlyFillMissing;
            maxRequestsPerMinute.value = config.MaxRequestsPerMinute;
            timeoutSeconds.value = config.TimeoutSeconds;
            clearError();
        } catch (error) {
            console.error('Number Cover configuration load failed.', error);
            showError('Unable to load configuration.');
        } finally {
            dashboard.hideLoadingMsg();
        }
    }

    page.addEventListener('viewshow', loadConfiguration);

    form.addEventListener('submit', async function (event) {
        event.preventDefault();
        dashboard.showLoadingMsg();
        try {
            let config = await apiClient.getPluginConfiguration(pluginId);
            config = withDefaults(config);
            config.EnableJableSource = enableJableSource.checked;
            config.OnlyFillMissing = onlyFillMissing.checked;
            config.MaxRequestsPerMinute = integerOrDefault(maxRequestsPerMinute.value, defaults.MaxRequestsPerMinute);
            config.TimeoutSeconds = integerOrDefault(timeoutSeconds.value, defaults.TimeoutSeconds);
            const result = await apiClient.updatePluginConfiguration(pluginId, config);
            clearError();
            dashboard.processPluginConfigurationUpdateResult(result);
        } catch (error) {
            console.error('Number Cover configuration save failed.', error);
            showError('Unable to save configuration.');
        } finally {
            dashboard.hideLoadingMsg();
        }
    });
}
