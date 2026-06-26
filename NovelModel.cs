using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Writing_App
{
    public class Chapter
    {
        public string Title { get; set; }
        public string Content { get; set; } = "";
        public Chapter Clone() => new Chapter { Title = this.Title + " (Copy)", Content = this.Content };
    }

    public class DraftSection
    {
        public string Title { get; set; }
        public bool IsDeletionAllowed { get; set; }
        public ObservableCollection<Chapter> Chapters { get; set; } = new ObservableCollection<Chapter>();

        public DraftSection Clone()
        {
            var clonedSection = new DraftSection { Title = this.Title + " (Copy)", IsDeletionAllowed = this.IsDeletionAllowed };
            foreach (var ch in this.Chapters) clonedSection.Chapters.Add(ch.Clone());
            return clonedSection;
        }
    }

    public class Novel
    {
        public string Title { get; set; }
        public ObservableCollection<DraftSection> Sections { get; set; } = new ObservableCollection<DraftSection>();

        public Novel Clone()
        {
            var clonedNovel = new Novel { Title = this.Title + " (Copy)" };
            foreach (var sec in this.Sections) clonedNovel.Sections.Add(sec.Clone());
            return clonedNovel;
        }
    }
}