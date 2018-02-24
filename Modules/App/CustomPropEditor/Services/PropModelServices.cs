﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using Catel.Collections;
using NLog;
using VixenModules.App.CustomPropEditor.Model;
using Point = System.Windows.Point;

namespace VixenModules.App.CustomPropEditor.Services
{
    public class PropModelServices
    {
        protected static Logger Logging = LogManager.GetCurrentClassLogger();
        private static PropModelServices _instance;
        private Prop _prop;
        private readonly Dictionary<Guid, ElementModel> _models = new Dictionary<Guid, ElementModel>();
        private readonly Dictionary<Guid, ElementModel> _lightToModel = new Dictionary<Guid, ElementModel>();

        private PropModelServices()
        {
            
        }

        public static PropModelServices Instance()
        {
            if (_instance == null)
            {
                _instance = new PropModelServices();
            }

            return _instance;
        }

        public Prop CreateProp(string name="Default")
        {
            _prop = new Prop(name);
            _models.Clear();
            _lightToModel.Clear();
            _models.Add(_prop.RootNode.Id, _prop.RootNode);
            return _prop;
        }

        public void SetImage(string filePath)
        {
            try
            {
                var image = new BitmapImage(new Uri(filePath, UriKind.Absolute));
                Prop.Image = image;
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "An error occured loading the image for a prop.");
            }
        }

        public IEnumerable<ElementModel> GetLeafNodes()
        {
            return _prop.GetLeafNodes();
        }

        public ElementModel CreateNode(string name, ElementModel parent = null)
        {
            if (parent == null)
            {
                parent = _prop.RootNode;
            }
            ElementModel em = new ElementModel(Uniquify(name), parent);
            parent.AddChild(em);
            _models.Add(em.Id, em);
            return em;
        }

        public void CreateGroupForElementModels(string name, IEnumerable<ElementModel> elementModels)
        {
            var em = CreateNode(name);
            foreach (var elementModel in elementModels)
            {
                em.Children.Add(elementModel);
                elementModel.Parents.Add(em);
            }
        }

        /// <summary>
        /// Removes element models and cleans up thier parent / child references
        /// </summary>
        /// <param name="elementModels"></param>
        public void RemoveElementModels(IEnumerable<ElementModel> elementModels)
        {
            //Remove the leaf nodes first.
            foreach (var elementModel in elementModels.OrderByDescending(x => x.IsLeaf).ToList())
            {
                if (!elementModel.IsLeaf)
                {
                    //clear any children references
                    foreach (var child in elementModel.Children.ToList())
                    {
                        RemoveChildFromParent(elementModel, child);
                    }
                }

                //Remove from the parents
                foreach (var parent in elementModel.Parents.ToList())
                {
                    RemoveChildFromParent(parent, elementModel);
                }

                //Clean up any lights
                foreach (var elementModelLight in elementModel.Lights)
                {
                    _lightToModel.Remove(elementModelLight.Id);
                }
                elementModel.Lights.Clear();
                _models.Remove(elementModel.Id);
            }
        }

        public void RemoveChildFromParent(ElementModel parent, ElementModel child)
        {
            Prop.RemoveFromParent(child, parent);
        }

        public void AddLightNode(ElementModel target, Point p, int? order = null, int? size=null)
        {
            if (target == null)
            {
                target = _prop.RootNode;
            }
            else if (target.Lights.Any())
            {
                throw new ArgumentException("Cannot add light node to leaf element with lights.");
            }

            if (order == null)
            {
                order = GetNextOrder();
            }

            ElementModel em = new ElementModel(Uniquify($"{target.Name}-{order}"), order.Value, target);
            target.AddChild(em);
            _models.Add(em.Id, em);
            if (size == null)
            {
                size = em.LightSize;
            }

            var light = CreateLight(p, size.Value);
            em.AddLight(light);
            _lightToModel.Add(light.Id, em);

        }

        public void AddLight(ElementModel target, Point p, int? order=null)
        {
            if (target == null)
            {
                target = _prop.RootNode;
            }
            else if (target.IsLeaf && target.Parents.Any())
            {
                AddLight(target, p);
            }

            if (order == null)
            {
                order = GetNextOrder();
            }
            
            ElementModel em = new ElementModel(Uniquify($"{target.Name}-{order}"), order.Value, target);
            target.AddChild(em);
            _models.Add(em.Id, em);

            var light = CreateLight(p, em.LightSize);
            em.AddLight(light);
            _lightToModel.Add(light.Id, em);
            
        }

        public void RemoveLights(IEnumerable<Light> lights)
        {
            foreach (var light in lights)
            {
                RemoveLight(light);
            }
        }

        public void RemoveLight(Light light)
        {
            if (light.ParentModelId != Guid.Empty)
            {
                ElementModel em;
                if (_models.TryGetValue(light.ParentModelId, out em))
                {
                    RemoveLight(em, light);
                }
            }
        }

        public void RemoveLight(ElementModel target, Light light)
        {
            if (target == null)
            {
                throw  new ArgumentNullException(nameof(target));
            }

            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }

            if (target.IsLeaf)
            {
                var success = target.RemoveLight(light);
                _lightToModel.Remove(light.Id);
                if(success)
                {
                    if (!target.Lights.Any())
                    {
                        //remove me from all my parents so I can be deleted
                        foreach (var parent in target.Parents.ToList())
                        {
                            RemoveChildFromParent(parent, target);
                        }

                        //Remove me
                        _models.Remove(target.Id);
                    }
                }
            }
        }

        public IEnumerable<ElementModel> FindModelsForLights(IEnumerable<Light> lights)
        {
            return FindModelsForLightIds(lights.Select(l => l.Id));
        }

        public IEnumerable<ElementModel> FindModelsForLightIds(IEnumerable<Guid> lightIds)
        {
            return lightIds.Where(_lightToModel.ContainsKey).Select(x => _lightToModel[x]);
        }

        public Prop Prop => _prop;

        private Light CreateLight(Point p, double size)
        {
            return new Light(p, size);
        }

        public void DeselectAllModels()
        {
            _models.Values.ForEach(m => m.IsSelected = false);
        }

        public bool IsNameDuplicated(string name)
        {
            return _models.Values.Count(x => x.Name == name)>1;
        }

        public string Uniquify(string name)
        {
            if (_models.Values.Any(x => x.Name == name))
            {
                string originalName = name;
                bool unique;
                int counter = 2;
                do
                {
                    name = $"{originalName} - {counter++}";
                    unique = _models.Values.All(x => x.Name != name);
                } while (!unique);
            }
            return name;
        }

        private int GetNextOrder()
        {
            return _prop.RootNode.GetLeafEnumerator().Max(x => x.Order) + 1;
        }

    }
}
