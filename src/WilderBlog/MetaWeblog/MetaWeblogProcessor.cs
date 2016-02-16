﻿using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
using WilderBlog.Data;
using WilderBlog.Helpers;
using XmlRpcLight;
using XmlRpcLight.Attributes;

namespace WilderBlog.MetaWeblog
{
  public class MetaWeblogProcessor : XmlRpcService
  {
    private IWilderRepository _repo;
    private UserManager<WilderUser> _userMgr;
    private IConfigurationRoot _config;
    private string _mediaPath;
    private IApplicationEnvironment _appEnv;

    public MetaWeblogProcessor(UserManager<WilderUser> userMgr, IWilderRepository repo, IConfigurationRoot config, IApplicationEnvironment appEnv)
    {
      _repo = repo;
      _userMgr = userMgr;
      _config = config;
      _appEnv = appEnv;

      _mediaPath = _config["MetaWeblog:StoragePath"];
      if (string.IsNullOrEmpty(_mediaPath) == true)
      {
        throw new InvalidOperationException(@"You need an AppSettings key named ""MetaWeblogAPIStoragePath"" that tells me where to put your files!");
      }

    }

    [XmlRpcMethod("metaWeblog.newPost")]
    public string AddPost(string blogid, string username, string password, Post post, bool publish)
    {
      EnsureUser(username, password).Wait();

      var newStory = new BlogStory();
      try
      {
        newStory.Title = post.title;
        newStory.Body = post.description;
        newStory.DatePublished = post.dateCreated;
        newStory.Categories = string.Join(",", post.categories);
        newStory.IsPublished = publish;
        newStory.Slug = newStory.GetStoryUrl();
        newStory.UniqueId = newStory.Slug;

        _repo.AddStory(newStory);
        _repo.SaveAll();
      }
      catch (Exception)
      {
        throw new XmlRpcFaultException(0, "Failed to save the post.");
      }
      return newStory.Id.ToString();

    }

    [XmlRpcMethod("metaWeblog.editPost")]
    public bool EditPost(string postid, string username, string password, Post post, bool publish)
    {
      EnsureUser(username, password).Wait();

      try
      {
        var story = _repo.GetStory(int.Parse(postid));

        story.Title = post.title;
        story.Body = post.description;
        story.DatePublished = post.dateCreated;
        story.Categories = string.Join(",", post.categories);
        story.IsPublished = publish;
        story.Slug = story.GetStoryUrl();

        _repo.SaveAll();

        return true;
      }
      catch (Exception)
      {
        throw new XmlRpcFaultException(0, "Failed to save the post.");
      }
    }

    [XmlRpcMethod("metaWeblog.getPost")]
    public Post GetPost(string postid, string username, string password)
    {
      EnsureUser(username, password).Wait();

      try
      {
        var story = _repo.GetStory(int.Parse(postid));
        var newPost = new Post()
        {
          title = story.Title,
          description = story.Body,
          dateCreated = story.DatePublished,
          categories = story.Categories.Split(','),
          postid = story.Id,
          userid = "shawnwildermuth",
          wp_slug = story.GetStoryUrl()
        };

        return newPost;
      }
      catch (Exception)
      {
        throw new XmlRpcFaultException(0, "Failed to get the post.");
      }
    }

    [XmlRpcMethod("metaWeblog.newMediaObject")]
    public MediaObjectInfo NewMediaObject(string blogid, string username, string password, MediaObject mediaObject)
    {
      EnsureUser(username, password).Wait();

      var filenameonly = mediaObject.name.Substring(mediaObject.name.LastIndexOf('/') + 1);

      var newPath = Path.Combine(_mediaPath, filenameonly).Replace("/", "\\");

      var filePath = Path.Combine(_appEnv.ApplicationBasePath, "wwwroot", newPath.StartsWith("\\") ? newPath.Substring(1) : newPath);

      // Make sure the directory exists
      var dirPath = Path.GetDirectoryName(filePath);
      EnsureDirectory(new DirectoryInfo(dirPath));

      // If the file exists, just punt
      if (File.Exists(filePath))
      {
        var nonCollidingName = string.Concat(Guid.NewGuid(), ".", Path.GetExtension(filePath));
        newPath = Path.Combine(_mediaPath, nonCollidingName);
        filePath = Path.Combine(_appEnv.ApplicationBasePath, "wwwroot", newPath);
      }

      // Write the file.
      File.WriteAllBytes(filePath, mediaObject.bits);

      // Create the response
      MediaObjectInfo objectInfo = new MediaObjectInfo();

      objectInfo.url = newPath;

      return objectInfo;
    }

    [XmlRpcMethod("metaWeblog.getCategories")]
    public CategoryInfo[] GetCategories(string blogid, string username, string password)
    {
      EnsureUser(username, password).Wait();

      return _repo.GetCategories()
        .Select(c => new CategoryInfo()
        {
          categoryid = c,
          title = c,
          description = c,
          htmlUrl = string.Concat("http://wildermuth.com/tags/", c),
          rssUrl = ""
        }).ToArray();
                  
    }

    [XmlRpcMethod("metaWeblog.getRecentPosts")]
    public Post[] GetRecentPosts(string blogid, string username, string password, int numberOfPosts)
    {
      EnsureUser(username, password).Wait();

      return _repo.GetStories(numberOfPosts).Select(s => new Post()
      {
        title = s.Title,
        description = s.Body,
        categories = s.Categories.Split(','),
        dateCreated = s.DatePublished,
        postid = s.Id,
        permalink = s.GetStoryUrl(),
        wp_slug = s.Slug
      }).ToArray();
    }

    [XmlRpcMethod("blogger.deletePost")]
    public bool DeletePost(string key, string postid, string username, string password, bool publish)
    {
      EnsureUser(username, password).Wait();

      try
      {
        var result = _repo.DeleteStory(postid);
        _repo.SaveAll();
        return true;
      }
      catch (Exception)
      {
        return false;
      }
    }

    [XmlRpcMethod("blogger.getUsersBlogs")]
    public BlogInfo[] GetUsersBlogs(string key, string username, string password)
    {
      EnsureUser(username, password).Wait();

      var blog = new BlogInfo()
      {
        blogid = "stw",
        blogName = "Shawn Wildermuth's Rants and Raves",
        url = "/"
      };

      return new BlogInfo[] { blog };
    }

    [XmlRpcMethod("blogger.getUserInfo")]
    public UserInfo GetUserInfo(string key, string username, string password)
    {
      EnsureUser(username, password).Wait();

      return new UserInfo()
      {
        email = "shawn@wildermuth.com",
        lastname = "Shawn",
        firstname = "Wildermuth",
        userid = "shawnwildermuth",
        url = "http://wildermuth.com"
      };
    }

    async Task EnsureUser(string username, string password)
    {
      var user = await _userMgr.FindByNameAsync(username);
      if (user != null)
      {
        if (await _userMgr.CheckPasswordAsync(user, password))
        {
          return;
        }
      }

      throw new XmlRpcFaultException(0, "Authentication failed.");
    }

    void EnsureDirectory(DirectoryInfo dir)
    {
      if (dir.Parent != null)
      {
        EnsureDirectory(dir.Parent);
      }

      if (!dir.Exists)
      {
        dir.Create();
      }
    }

  }
}