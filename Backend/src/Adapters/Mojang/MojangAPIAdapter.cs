﻿using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Fork.Logic.Managers;
using Fork.Logic.Model.Web.Mojang;
using Fork.Util.ExtensionMethods;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Fork.Adapters.Mojang;

public class MojangApiAdapter : AbstractAdapter, IMojangApiAdapter
{
    public MojangApiAdapter(ILogger<MojangApiAdapter> logger, IApplicationManager applicationManager) : base(logger,
        applicationManager)
    {
    }

    public async Task<string?> UidForNameAsync(string name)
    {
        string encodedName = Uri.EscapeDataString(name);
        PlayerByName? response =
            await GetAsync<PlayerByName>($"https://api.mojang.com/users/profiles/minecraft/{encodedName}");
        return response?.Id;
    }

    public async Task<PlayerProfile?> ProfileForUidAsync(string uid)
    {
        string encodedUid = Uri.EscapeDataString(uid.Replace("-", ""));
        PlayerProfile? response =
            await GetAsync<PlayerProfile>($"https://sessionserver.mojang.com/session/minecraft/profile/{encodedUid}");
        return response;
    }

    /// <summary>
    ///     This method will retrieve the Texture of a player from its encoded profile and build a base64 representation of the
    ///     Head
    /// </summary>
    public async Task<string> Base64HeadFromTextureProperty(string? encodedTextureProfile)
    {
        await using MemoryStream imageStream = new();

        if (encodedTextureProfile == null)
        {
            string defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "DefaultHead.png");
            using Image img = await Image.LoadAsync(defaultPath);
            await img.SaveAsPngAsync(imageStream);
        }
        else
        {
            byte[] profileJson = Convert.FromBase64String(encodedTextureProfile);
            string profileJsonString = Encoding.UTF8.GetString(profileJson).Replace("\n", "");
            PlayerTextureProfile? profile = JsonConvert.DeserializeObject<PlayerTextureProfile>(profileJsonString);


            if (profile?.Textures?.Skin?.Url is not { } url)
            {
                string defaultPath =
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "DefaultHead.png");
                using Image img = await Image.LoadAsync(defaultPath);
                await img.SaveAsPngAsync(imageStream);
            }
            else
            {
                using HttpClient client = new();
                Uri uri = new(url);
                HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                while (response.StatusCode == (HttpStatusCode)429)
                {
                    await Task.Delay(5000);
                    response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                }

                response.EnsureSuccessStatusCode();
                await using Stream respStream = await response.Content.ReadAsStreamAsync();
                using Image img = await Image.LoadAsync(respStream);
                Image headOverlay = img.Clone(x => x.Crop(new Rectangle(40, 8, 8, 8)));
                img.Mutate(x =>
                {
                    x.Crop(new Rectangle(8, 8, 8, 8))
                        .DrawImage(headOverlay, new Point(0, 0), 1f);
                    headOverlay.Dispose();
                });
                await img.SaveAsPngAsync(imageStream);
            }
        }

        imageStream.Position = 0;
        return imageStream.ConvertToBase64();
    }
}