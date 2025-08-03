using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IntegrationTests
{
    [TestFixture]
    public class WpfInspectorIntegrationTests
    {
        private Process? _testAppProcess;
        private Process? _injectorProcess;
        private HttpClient? _httpClient;
        private bool _testStartedTestApp;
        private bool _testStartedInjector;
        private const string InjectorUrl = "http://localhost:8080";
        private const int HttpRequestTimeoutMs = 30000;

        [SetUp]
        public void Setup()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMilliseconds(HttpRequestTimeoutMs);
            _testStartedTestApp = false;
            _testStartedInjector = false;
        }

        [TearDown]
        public void TearDown()
        {
            StopProcesses();
            _httpClient?.Dispose();
        }

        [Test]
        public async Task IntegrationTest_StartProcesses_CallGetProcessAndGetVisualTree_ValidateResponse()
        {
            // NOTE: For debugging, you can manually start TestApp or Injector from the IDE with debugger attached,
            // then run this test with 'dotnet test'. The test will detect existing processes and use them instead
            // of starting new ones, allowing you to debug while the integration test runs.
            // 1. Start TestApp
            await StartTestAppIfNotRunningAsync();
            
            // 2. Start Injector
            await StartInjectorIfNotRunningAsync();
            
            // 3. Send HTTP request to call get_wpf_processes
            var processesResponse = await CallMcpToolAsync("get_wpf_processes", new { });
            Assert.That(processesResponse, Is.Not.Null, "get_wpf_processes response should not be null");
            
            // 4. Extract the PID of TestApp
            var testAppPid = ExtractTestAppPid(processesResponse);
            Assert.That(testAppPid, Is.GreaterThan(0), "TestApp PID should be found and valid");
            
            // 5. Send HTTP request to get_visual_tree
            var visualTreeResponse = await CallMcpToolAsync("get_visual_tree", new { processId = testAppPid.ToString() });
            Assert.That(visualTreeResponse, Is.Not.Null, "get_visual_tree response should not be null");
            
            // 6. Assert that the reply matches the expected JSON structure
            await ValidateVisualTreeResponse(visualTreeResponse);
        }

        private async Task StartTestAppIfNotRunningAsync()
        {
            // Check if TestApp is already running
            var existingTestApp = Process.GetProcessesByName("TestApp").FirstOrDefault();
            if (existingTestApp != null)
            {
                TestContext.WriteLine($"TestApp already running with PID: {existingTestApp.Id}");
                _testAppProcess = existingTestApp;
                _testStartedTestApp = false;
                return;
            }

            var testAppPath = GetTestAppExecutablePath();
            Assert.That(File.Exists(testAppPath), Is.True, $"TestApp executable not found at: {testAppPath}");

            _testAppProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = testAppPath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                }
            };

            var started = _testAppProcess.Start();
            Assert.That(started, Is.True, "Failed to start TestApp process");

            // Wait for the WPF application to initialize
            await Task.Delay(3000);
            
            Assert.That(_testAppProcess.HasExited, Is.False, "TestApp process should still be running");
            _testStartedTestApp = true;
            TestContext.WriteLine($"TestApp started with PID: {_testAppProcess.Id}");
        }

        private async Task StartInjectorIfNotRunningAsync()
        {
            // Check if Injector is already running by testing health endpoint
            try
            {
                var response = await _httpClient!.GetAsync($"{InjectorUrl}/mcp/health");
                if (response.IsSuccessStatusCode)
                {
                    TestContext.WriteLine("Injector already running (health check passed)");
                    // Try to find the existing process for cleanup purposes
                    _injectorProcess = Process.GetProcessesByName("Injector").FirstOrDefault();
                    _testStartedInjector = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Health check failed, starting new Injector: {ex.Message}");
            }

            var injectorPath = GetInjectorExecutablePath();
            Assert.That(File.Exists(injectorPath), Is.True, $"Injector executable not found at: {injectorPath}");

            _injectorProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = injectorPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var started = _injectorProcess.Start();
            Assert.That(started, Is.True, "Failed to start Injector process");

            // Wait for the HTTP server to start
            await WaitForInjectorHealthAsync();
            
            Assert.That(_injectorProcess.HasExited, Is.False, "Injector process should still be running");
            _testStartedInjector = true;
            TestContext.WriteLine($"Injector started with PID: {_injectorProcess.Id}");
        }

        private async Task WaitForInjectorHealthAsync()
        {
            var maxAttempts = 20;
            var delayMs = 500;
            
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var response = await _httpClient!.GetAsync($"{InjectorUrl}/mcp/health");
                    if (response.IsSuccessStatusCode)
                    {
                        TestContext.WriteLine("Injector health check passed");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine($"Health check attempt {i + 1} failed: {ex.Message}");
                }
                
                await Task.Delay(delayMs);
            }
            
            Assert.Fail("Injector did not become healthy within the expected time");
        }

        private async Task<JObject> CallMcpToolAsync(string toolName, object arguments)
        {
            var json = JsonConvert.SerializeObject(arguments);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient!.PostAsync($"{InjectorUrl}/mcp/tools/{toolName}", content);
            Assert.That(response.IsSuccessStatusCode, Is.True, $"HTTP request to {toolName} failed with status: {response.StatusCode}");
            
            var responseText = await response.Content.ReadAsStringAsync();
            TestContext.WriteLine($"Response from {toolName}: {responseText}");
            
            var responseJson = JObject.Parse(responseText);
            Assert.That(responseJson["success"]?.Value<bool>(), Is.True, $"MCP tool {toolName} reported failure");
            
            return responseJson;
        }

        private int ExtractTestAppPid(JObject processesResponse)
        {
            var resultText = processesResponse["result"]?.ToString();
            Assert.That(resultText, Is.Not.Null.And.Not.Empty, "Processes result should not be empty");
            
            // Parse the result as JSON - it's a nested JSON structure
            var resultJson = JObject.Parse(resultText);
            var processesArray = resultJson["processes"] as JArray;
            
            Assert.That(processesArray, Is.Not.Null, "Processes array should not be null");
            
            foreach (var process in processesArray)
            {
                var processName = process["processName"]?.ToString();
                if (processName != null && processName.Contains("TestApp"))
                {
                    var pid = process["processId"]?.Value<int>();
                    if (pid.HasValue && pid.Value > 0)
                    {
                        TestContext.WriteLine($"Found TestApp with PID: {pid.Value}");
                        return pid.Value;
                    }
                }
            }
            
            Assert.Fail("TestApp process not found in the processes list");
            return 0;
        }

        private async Task ValidateVisualTreeResponse(JObject visualTreeResponse)
        {
            var resultText = visualTreeResponse["result"]?.ToString();
            Assert.That(resultText, Is.Not.Null.And.Not.Empty, "Visual tree result should not be empty");
            
            // Load expected JSON from file
            var expectedJsonPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "get_visual_tree.json");
            if (!File.Exists(expectedJsonPath))
            {
                // Create the reference file with the actual response for first-time setup
                await File.WriteAllTextAsync(expectedJsonPath, resultText);
                TestContext.WriteLine($"Created reference file: {expectedJsonPath}");
                TestContext.WriteLine("Please review the generated reference file and re-run the test");
                Assert.Inconclusive("Reference file created. Please review and re-run the test.");
                return;
            }
            
            var expectedJson = await File.ReadAllTextAsync(expectedJsonPath);
            var expectedObject = JObject.Parse(expectedJson);
            var actualObject = JObject.Parse(resultText);
            
            // Validate structure rather than exact content (since some values may vary)
            ValidateJsonStructure(expectedObject, actualObject, "root");
        }

        private void ValidateJsonStructure(JToken expected, JToken actual, string path)
        {
            Assert.That(actual.Type, Is.EqualTo(expected.Type), $"Type mismatch at path: {path}");
            
            switch (expected.Type)
            {
                case JTokenType.Object:
                    var expectedObj = (JObject)expected;
                    var actualObj = (JObject)actual;
                    
                    foreach (var property in expectedObj.Properties())
                    {
                        Assert.That(actualObj.ContainsKey(property.Name), Is.True, 
                            $"Missing property '{property.Name}' at path: {path}");
                        
                        // Skip value comparison for processId, hashCode, and dataContextHashCode - only verify field exists
                        if (property.Name.Equals("processId", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("hashCode", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase) ||
                            property.Name.Equals("dataContextHashCode", StringComparison.OrdinalIgnoreCase))
                        {
                            TestContext.WriteLine($"Skipping value comparison for field '{property.Name}' at path: {path}");
                            continue;
                        }
                        
                        ValidateJsonStructure(property.Value, actualObj[property.Name]!, 
                            $"{path}.{property.Name}");
                    }
                    break;
                    
                case JTokenType.Array:
                    var expectedArray = (JArray)expected;
                    var actualArray = (JArray)actual;
                    
                    if (expectedArray.Count > 0 && actualArray.Count > 0)
                    {
                        // Validate structure of first element
                        ValidateJsonStructure(expectedArray[0], actualArray[0], $"{path}[0]");
                    }
                    break;
                    
                case JTokenType.String:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Boolean:
                case JTokenType.Null:
                    // For primitive values, compare both type and value
                    Assert.That(actual.ToString(), Is.EqualTo(expected.ToString()), 
                        $"Value mismatch at path: {path}. Expected: '{expected}', Actual: '{actual}'");
                    break;
            }
        }

        private void StopProcesses()
        {
            try
            {
                if (_testAppProcess != null && !_testAppProcess.HasExited && _testStartedTestApp)
                {
                    _testAppProcess.Kill();
                    _testAppProcess.WaitForExit(5000);
                    _testAppProcess.Dispose();
                    TestContext.WriteLine("TestApp process stopped (was started by test)");
                }
                else if (_testAppProcess != null && !_testStartedTestApp)
                {
                    TestContext.WriteLine("TestApp process left running (was not started by test)");
                    _testAppProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Error stopping TestApp: {ex.Message}");
            }

            try
            {
                if (_injectorProcess != null && !_injectorProcess.HasExited && _testStartedInjector)
                {
                    _injectorProcess.Kill();
                    _injectorProcess.WaitForExit(5000);
                    _injectorProcess.Dispose();
                    TestContext.WriteLine("Injector process stopped (was started by test)");
                }
                else if (_injectorProcess != null && !_testStartedInjector)
                {
                    TestContext.WriteLine("Injector process left running (was not started by test)");
                    _injectorProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Error stopping Injector: {ex.Message}");
            }
        }

        private string GetTestAppExecutablePath()
        {
            var currentDir = TestContext.CurrentContext.TestDirectory;
            var solutionDir = Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
            
            if (solutionDir == null)
                throw new DirectoryNotFoundException("Could not locate solution directory");
            
            var testAppPath = Path.Combine(solutionDir, "MCP", "TestApp", "bin", "Debug", "net8.0-windows", "TestApp.exe");
            
            if (!File.Exists(testAppPath))
            {
                // Try Release configuration
                testAppPath = Path.Combine(solutionDir, "MCP", "TestApp", "bin", "Release", "net8.0-windows", "TestApp.exe");
            }
            
            return testAppPath;
        }

        private string GetInjectorExecutablePath()
        {
            var currentDir = TestContext.CurrentContext.TestDirectory;
            var solutionDir = Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.Parent?.FullName;
            
            if (solutionDir == null)
                throw new DirectoryNotFoundException("Could not locate solution directory");
            
            var injectorPath = Path.Combine(solutionDir, "MCP", "Injector", "bin", "Debug", "net8.0-windows", "Injector.exe");
            
            if (!File.Exists(injectorPath))
            {
                // Try Release configuration
                injectorPath = Path.Combine(solutionDir, "MCP", "Injector", "bin", "Release", "net8.0-windows", "Injector.exe");
            }
            
            return injectorPath;
        }
    }
}