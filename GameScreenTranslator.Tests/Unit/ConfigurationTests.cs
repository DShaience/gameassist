using Xunit;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace GameScreenTranslator.Tests.Unit;

public class ConfigurationTests
{
    [Fact]
    public void AppSettings_LoadsFromJson()
    {
        // Arrange: Create a test configuration (Azure Foundry for LLM/Vision + Azure Speech for TTS/STT)
        var configJson = @"{
            ""Providers"": {
                ""AzureFoundry"": {
                    ""Endpoint"": ""https://example-foundry.inference.ai.azure.com"",
                    ""ApiKey"": ""foundry-key-123"",
                    ""ApiVersion"": ""2024-10-21"",
                    ""LLM"": {
                        ""ChatDeployment"": ""chat-deploy"",
                        ""VisionDeployment"": ""vision-deploy"",
                        ""RetryAttempts"": 2,
                        ""TimeoutSeconds"": 30
                    }
                },
                ""AzureSpeech"": {
                    ""ConfigPath"": ""speech_language_config.json""
                }
            },
            ""Translation"": {
                ""TargetLanguage"": ""Hebrew"",
                ""Mode"": ""explain""
            }
        }";
        
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, configJson);
        
        try
        {
            // Act: Load configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(tempFile)
                .Build();
            
            // Assert
            Assert.Equal("https://example-foundry.inference.ai.azure.com", configuration["Providers:AzureFoundry:Endpoint"]);
            Assert.Equal("foundry-key-123", configuration["Providers:AzureFoundry:ApiKey"]);
            Assert.Equal("chat-deploy", configuration["Providers:AzureFoundry:LLM:ChatDeployment"]);
            Assert.Equal("vision-deploy", configuration["Providers:AzureFoundry:LLM:VisionDeployment"]);
            Assert.Equal("speech_language_config.json", configuration["Providers:AzureSpeech:ConfigPath"]);
            Assert.Equal("Hebrew", configuration["Translation:TargetLanguage"]);
            Assert.Equal("explain", configuration["Translation:Mode"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
    
    [Fact]
    public void HotkeyConfig_ParsesCtrlAltT()
    {
        // Arrange
        var hotkeyString = "Ctrl+Alt+T";
        
        // Act
        var config = GameScreenTranslator.Core.Configuration.HotkeyConfig.Parse(hotkeyString);
        
        // Assert
        Assert.Equal(GameScreenTranslator.Core.Configuration.ModifierKeys.Control | 
                     GameScreenTranslator.Core.Configuration.ModifierKeys.Alt, 
                     config.Modifiers);
        Assert.Equal(GameScreenTranslator.Core.Configuration.VirtualKey.T, config.Key);
    }
    
    [Theory]
    [InlineData("Shift+F5", GameScreenTranslator.Core.Configuration.ModifierKeys.Shift, GameScreenTranslator.Core.Configuration.VirtualKey.F5)]
    [InlineData("Ctrl+S", GameScreenTranslator.Core.Configuration.ModifierKeys.Control, GameScreenTranslator.Core.Configuration.VirtualKey.S)]
    [InlineData("Alt+Enter", GameScreenTranslator.Core.Configuration.ModifierKeys.Alt, GameScreenTranslator.Core.Configuration.VirtualKey.Enter)]
    public void HotkeyConfig_ParsesVariousHotkeys(string input, GameScreenTranslator.Core.Configuration.ModifierKeys expectedMods, GameScreenTranslator.Core.Configuration.VirtualKey expectedKey)
    {
        // Act
        var config = GameScreenTranslator.Core.Configuration.HotkeyConfig.Parse(input);
        
        // Assert
        Assert.Equal(expectedMods, config.Modifiers);
        Assert.Equal(expectedKey, config.Key);
    }
    
    [Fact]
    public void HotkeyConfig_ThrowsOnInvalidInput()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => GameScreenTranslator.Core.Configuration.HotkeyConfig.Parse(""));
        Assert.Throws<ArgumentException>(() => GameScreenTranslator.Core.Configuration.HotkeyConfig.Parse("T"));
        Assert.Throws<ArgumentException>(() => GameScreenTranslator.Core.Configuration.HotkeyConfig.Parse("InvalidMod+T"));
    }
}
