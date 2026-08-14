namespace ModManager.Tests.Infrastructure.Services.Sacrificial;

/// <summary>
/// Fixtures for <c>SacrificialSiteStrategy</c> tests. <see cref="SixModCardsSlice"/> is a verbatim
/// excerpt of the real sacrificialmods.com/downloads.html markup (captured 2026-08-13) — six
/// consecutive mod cards plus the ad-container blocks and HTML comments that sit between them on the
/// real page, so the parser is tested against actual structure rather than an idealized guess at it.
/// </summary>
internal static class SacrificialFixtures
{
    public const string SixModCardsSlice = """
    <div class="mod-card" id="ExtremeViolenceDownload" data-search-title="Extreme Violence" data-category="violence-survival" data-category-marker="true" data-color="#e11d48">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="ExtremeViolenceDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_ExtremeViolence%20-MOD-%20V2.6.3.2.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=141061260&m=547364630" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo" /></a>
        <a href="https://sacrificialmods.com/extreme-violence-v2.6.2-news.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/extreme-violence-smaller-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/watch?v=byfDJ5eg3ZQ" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/sac_extreme-violence--mod--v2.6.2-thumbnail-background%20Small.jpg" alt="Extreme Violence Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/sac_extreme-violence--mod--v2.6.2-thumbnail-background.webp" />
        <span class="version-badge">v2.6.3.2</span>
      </div>
      <div class="mod-text-content">
        <h3 class="mod-title">Extreme Violence</h3>
        <p class="mod-description">Kill sims in 40+ different ways, including with firearms, knives, and other melee weapons. Turn violent and beat up sims by punching, kicking, and more. Develop a horrible reputation by becoming the most hated and feared sim in town, and join gangs or start a feud with them.</p>
      </div>
      <div class="update-info-column">
         <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>10-12-2025</B></span></div>
         <div class="update-info-item">Reason: <span style="color: #fdfdfd;">Added 33 new getaway activites for Adventure Awaits Expansion Pack.</span></div>
         <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
     </div>
    </div>

    <div class="mod-card" id="LifeTragediesDownload" data-search-title="Life Tragedies" data-category="violence-survival">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="LifeTragediesDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Life%20Tragedies%20-MOD-%20v1.3.9.3.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=35725766&m=629936626" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo"/></a>
        <a href="https://sacrificialmods.com/life-tragedies-release-notes.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/life-tragedies-small-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/watch?v=RGBNBddcGyw" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/life%20tragedies%20-mod-%20v10%20e%20thumbnail%20Small.jpg" alt="Life Tragedies Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/life%20tragedies%20-mod-%20v10%20e%20thumbnail.webp" />
        <span class="version-badge">v1.3.9.3</span>
        <span class="status-badge badge-updatedd"></span>
      </div>
      <div class="mod-text-content">
        <h3 class="mod-title">Life Tragedies</h3>
        <p class="mod-description">Add dark realism to your Sims' lives. They will now face tragic occurrences that change their fates, including fatal illnesses, kidnapping, serial killers, armed robbers, car accidents, and bullying.</p>
      </div>
      <div class="update-info-column">
          <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>03-16-2026</B></span></div>
          <div class="update-info-item">Reason: <span style="color: #fdfdfd;">• Kidnapped Sims will no longer remain as ghosts when being rescued.<br><br>Fixed an issue related to Toddlers/Children's death & added a new death/ghost type (Death By Tragedy).</span></div>
          <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
      </div>
    </div>

    <div class="mod-card" id="ZombieApocalypseDownload" data-search-title="Zombie Apocalypse" data-category="violence-survival">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="ZombieApocalypseDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Zombie%20Apocalypse%20%20-MOD-%20v2.3.1.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=35725766&m=528910330" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo" /></a>
        <a href="https://sacrificialmods.com/zombie-apocalypse-news.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/zombie-apocalypse-small-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/watch?v=rERBmWte-CM" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/Zombie%20Apocalypse%20-MOD-%20v2.0%20%20Thumbnail%20background%20Small.jpg" alt="Zombie Apocalypse Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/Zombie%20Apocalypse%20-MOD-%20v2.0%20%20Thumbnail%20background.jpg" />
        <span class="version-badge">v2.3.1</span>
        <span class="status-badge badge-updatedd"></span>
      </div>
      <div class="mod-text-content">
        <h3 class="mod-title">Zombie Apocalypse</h3>
        <p class="mod-description">Survive a world full of the undead. Take the role of a heroic survivor, or become the villain and spread the virus. Getting infected grants your Sim the ability to be a playable zombie who's thirsty for flesh.</p>
      </div>
      <div class="update-info-column">
          <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>09-7-2025</B></span></div>
          <div class="update-info-item">Reason: <span style="color: #fdfdfd;">Better compatibility with other script mods after 08-19-2025 patch.</span></div>
          <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
      </div>
    </div>

    <div class="ad-container">
      <span style="position:absolute; top:5px; right:15px; color:#555; font-size:0.6rem; z-index:2;">AD</span>
      <ins class="adsbygoogle" style="display: block; width: 100%;" data-ad-format="fluid" data-ad-layout-key="-fb+5w+4e-db+86" data-ad-client="ca-pub-4603309619969104" data-ad-slot="8141017694"></ins>
      <script>(adsbygoogle = window.adsbygoogle || []).push({});</script>
    </div>

    <!-- EPIC -->
    <div class="mod-card" id="PathOfLegendsDownload" data-search-title="Path Of Legends" data-category="epic" data-category-marker="true" data-color="#4a0cf5">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="PathOfLegendsDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/Kyutso_Path%20Of%20Legends%20-Mod-%20v1.2.1.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=35725766&m=528910575" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo" /></a>
        <a href="https://sacrificialmods.com/kyutso-path-of-legends-release-notes.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/path-of-legends-small-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/watch?v=B8j2UD1G3tk" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/path%20of%20legends%20Small.jpg" alt="Path Of Legends Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/path%20of%20legends.jpeg" />
        <span class="version-badge">v1.2.1</span>
        <span class="status-badge badge-updatedd"></span>
      </div>
      <div class="mod-text-content">
        <h3 class="mod-title">Path Of Legends By Kyutso</h3>
        <p class="mod-description">Become a Katana or a Greatsword master and assassinate enemies and marked Sims. Take the role of a Katana-wielding Ninja or a Greatsword-wielding Warrior to defeat enemies of the rival clan and use your skills to assassinate sims who are marked for death.</p>
      </div>
      <div class="update-info-column">
          <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>09-7-2025</B></span></div>
          <div class="update-info-item">Reason: <span style="color: #fdfdfd;">Better compatibility with other script mods after 08-19-2025 patch.</span></div>
          <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
      </div>
    </div>

    <div class="mod-card" id="ArmageddonDownload" data-search-title="Armageddon" data-category="epic">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="ArmageddonDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Armageddon%20-MOD-%20v1.5.1.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=35725766&m=528910648" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo" /></a>
        <a href="https://sacrificialmods.com/armageddon-mod-news.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/armageddon-small-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/watch?v=4piHJIteS5M" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/armageddon%20mod%20Small.jpg" alt="Armageddon Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/armageddon%20mod%20(1).jpg" />
        <span class="version-badge">v1.5.1</span>
        <span class="status-badge badge-updatedd"></span>
      </div>
      <div class="mod-text-content">
        <div class="warning-container" onclick="toggleClickedText(event)">
          <img src="https://www.sacrificialmods.com/images/Eye%20Warning.png" alt="Warning" width="80" height="auto" />
          <div class="warning-text">Epilepsy Warning</div>
          <div class="clicked-warning-text" id="clickedWarningText">This mod contains intense flashing and unfortunately is not suitable for players with epilepsy.</div>
        </div>
        <h3 class="mod-title">Armageddon</h3>
        <p class="mod-description">Play as a Superhero and save the world, or a Supervillain and destroy it. As a hero, develop the Super Powers skill to save Sims from unfortunate events. As a villain, develop the dark powers skill to destroy lives and increase the world's corruption, influencing other Sims to perform evil actions.</p>
      </div>
      <div class="update-info-column">
          <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>09-7-2025</B></span></div>
          <div class="update-info-item">Reason: <span style="color: #fdfdfd;">Better compatibility with other script mods after 08-19-2025 patch.</span></div>
          <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
      </div>
    </div>

    <!-- NEW MID-CONTENT AD START -->
    <div class="ad-container">
        <ins class="adsbygoogle" style="display:block; width:100%;" data-ad-client="ca-pub-4603309619969104" data-ad-slot="4176198120" data-ad-format="auto" data-full-width-responsive="true"></ins>
        <script>(adsbygoogle = window.adsbygoogle || []).push({});</script>
    </div>
    <!-- NEW MID-CONTENT AD END -->

    <!-- FAME & DRAMA -->
    <div class="mod-card" id="RoadToFameDownload" data-search-title="Road To Fame" data-category="fame-drama" data-category-marker="true" data-color="#ffc300">
      <button class="copy-link-btn" data-tooltip="Copy Mod's Link" data-anchor="RoadToFameDownload" aria-label="Copy Mod Link">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z"/></svg>
      </button>
      <div class="mod-actions">
        <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Road%20To%20Fame%20-MOD-%20v0.5.1%20D12.zip" class="btn btn-primary" data-tooltip="Download directly from SacrificialMods.com"><span class="btn-text">Download</span><div class="btn-loader"></div></a>
        <a href="https://www.patreon.com/file?h=35725766&m=528910666" class="btn btn-patreon" data-tooltip="Download from Patreon.com (Alternative)"><span class="btn-text">Download</span><div class="btn-loader"></div><img src="https://sacrificialmods.com/Direct_Mod_Downloads/Patreon-logo-2013-2.png" alt="Patreon Logo" class="patreon-logo" /></a>
        <a href="https://sacrificialmods.com/road-to-fame-news.html" class="btn btn-secondary" target="_blank">Release Notes</a>
        <a href="https://sacrificialmods.com/road-to-fame-small-updates-release-notes.html" class="btn btn-secondary" target="_blank">Small Release Notes</a>
        <a href="https://www.youtube.com/results?search_query=sims+4+road+to+fame+mod" class="btn btn-secondary" target="_blank">Watch Video ▶</a>
      </div>
      <div class="mod-image-container">
        <img src="https://sacrificialmods.com/Modthumbnails/Road%20To%20Fame%20-MOD-%20V%200.5%20Thumbnail%20Background2%201920x1080%20Small.jpg" alt="Road To Fame Mod" class="mod-image" data-lightbox-src="https://sacrificialmods.com/Modthumbnails/Road%20To%20Fame%20-MOD-%20V%200.5%20Thumbnail%20Background2%201920x1080.jpg" />
        <span class="version-badge">v0.5.1 D12</span>
        <span class="status-badge badge-updatedd"></span>
      </div>
      <div class="mod-text-content">
        <h3 class="mod-title">Road To Fame</h3>
        <p class="mod-description">Give your sims the life of luxury and fame with fans, paparazzi, and promotional offers. Make your sim famous in 5 different tracks: Simstagram, Modeling, Acting, Professional Singing & Street Dancing! Each track has unique interactions and ways to earn money. You can even hire bodyguards and personal assistants.</p>
      </div>
      <div class="update-info-column">
          <div class="update-info-item">Last Update: <span style="color: #fdfdfd;"><B>09-7-2025</B></span></div>
          <div class="update-info-item">Reason: <span style="color: #fdfdfd;">Better compatibility with other script mods after 08-19-2025 patch.</span></div>
          <div class="update-info-item">Status: <span style="color: #3dd14e;"><B>Updated</B></span></div>
      </div>
    </div>
    """;

    /// <summary>
    /// Simulates a site redesign: no <c>mod-card</c> markup at all. The parser must return an empty
    /// observation list rather than throwing or guessing — this is what makes the base service report
    /// Indeterminate instead of a false UpToDate when a scraper silently breaks.
    /// </summary>
    public const string RedesignedPageWithNoModCards = """
    <!DOCTYPE html>
    <html>
    <body>
      <div class="new-catalog-grid">
        <article class="catalog-entry" data-id="zombie-apocalypse">
          <h2>Zombie Apocalypse</h2>
          <p>Version 2.3.2</p>
        </article>
      </div>
    </body>
    </html>
    """;
}
