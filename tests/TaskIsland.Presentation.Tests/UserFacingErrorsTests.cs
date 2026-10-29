using TaskIsland.Domain;
using TaskIsland.Presentation;
using Xunit;
public class UserFacingErrorsTests
{
 [Theory][InlineData("", "请输入日程内容。")][InlineData(" ","请输入日程内容。")]
 public void EmptyTitleHasNoImplementationParameter(string title,string expected)
 {var error=Assert.Throws<ArgumentException>(()=>TaskText.Normalize(title));Assert.Equal(expected,UserFacingErrors.Message(error));}
 [Fact] public void LongTitleHasNoImplementationParameter()
 {var error=Assert.Throws<ArgumentException>(()=>TaskText.Normalize(new string('字',201)));Assert.Equal("日程内容最多 200 个文字或表情。",UserFacingErrors.Message(error));}
}
